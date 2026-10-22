"""FastAPI application of the AI service (AI-03).

Run (from the repository root, after loading .env):
    ai/.venv/bin/uvicorn --app-dir ai/service --factory app:create_app --host 127.0.0.1 --port 8000

The service holds no data and computes no money: it passes text the backend already formatted to the
local model. Every error is a problem+json body shaped like the .NET API's (title, status, code, traceId).
At startup the model is loaded in the background (AI-15), so the first question does not wait for it.
"""

import json
import logging
import os
import threading
import uuid
from collections.abc import Callable, Mapping
from contextlib import asynccontextmanager

from fastapi import Depends, FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from starlette.exceptions import HTTPException

from answer_check import AnswerRejected
from contracts import (ChatRequest, ChatResponse, ExplanationRequest, ExplanationResponse, HealthResponse, SuggestionsResponse,
                       ToolCallOut)
from chat import ChatService, LangChainClarificationWriter, LangChainIntentClassifier, LangChainToolSelector
from explainer import Explainer, LangChainExplainer
from ollama_client import (
    ModelNotFound,
    OllamaBadResponse,
    OllamaError,
    OllamaSettings,
    OllamaTimeout,
    OllamaUnavailable,
    Readiness,
    check_ready,
    warm_up as load_model,
)
from mcp_gateway import McpSettings, McpToolGateway
from routing import Router
from suggestions import StarterQuestions
from tools import BackendTimeout, BackendUnavailable, SessionInvalid, ToolCallRejected

logger = logging.getLogger("bankingai.ai")

AI_UNAVAILABLE = ("ai_unavailable", "Asistan şu an kullanılamıyor.")
MODEL_MISSING = ("model_missing", "Asistan modeli bulunamadı.")
ANSWER_REJECTED = ("ai_answer_rejected", "Asistan cevabı doğrulanamadı.")
BACKEND_UNAVAILABLE = ("backend_unavailable", "Banka servisine şu an ulaşılamıyor.")
INTERNAL_ERROR = ("internal_error", "Beklenmeyen bir hata oluştu.")

# Status → code and title, the same table as ApiProblems in the .NET API.
_BY_STATUS = {
    400: ("validation_failed", "İstek geçersiz."),
    401: ("unauthorized", "Giriş gerekli."),
    404: ("not_found", "Bulunamadı."),
    405: ("method_not_allowed", "Bu adres bu yöntemi desteklemiyor."),
    500: INTERNAL_ERROR,
}

_OLLAMA_ERRORS: dict[type[OllamaError], tuple[int, str, str]] = {
    OllamaUnavailable: (503, *AI_UNAVAILABLE),
    ModelNotFound: (503, *MODEL_MISSING),
    OllamaTimeout: (504, "ai_timeout", "Asistan zamanında cevap veremedi."),
    OllamaBadResponse: (502, "ai_bad_response", "Asistan geçersiz cevap verdi."),
}

# Errors of the tool bridge (AI-14a, K4-A): each tells the caller something different to do.
_BRIDGE_ERRORS: dict[type[Exception], tuple[int, str, str]] = {
    SessionInvalid: (401, "session_invalid", "Oturum geçersiz; yeniden giriş yapın."),
    BackendUnavailable: (503, *BACKEND_UNAVAILABLE),
    ToolCallRejected: (502, "ai_bad_tool_call", "Asistan geçersiz bir sorgu üretti."),
    BackendTimeout: (504, "ai_timeout", "Asistan zamanında cevap veremedi."),
}

_NOT_READY = {Readiness.SERVER_UNAVAILABLE: AI_UNAVAILABLE, Readiness.MODEL_MISSING: MODEL_MISSING}

# LangChain sends every prompt to the LangSmith cloud when one of these is on; bank data must stay on this machine.
_TRACING_SETTINGS = ("LANGSMITH_TRACING", "LANGSMITH_TRACING_V2", "LANGCHAIN_TRACING", "LANGCHAIN_TRACING_V2")


def create_app(
    env: Mapping[str, str] | None = None, warm_up: Callable[[OllamaSettings], float] = load_model
) -> FastAPI:
    """Builds the service; fails at startup when the model setting is invalid or LangSmith tracing is on.

    ``warm_up`` loads the model when the server starts (tests pass a fake one).
    """
    env = os.environ if env is None else env
    _refuse_cloud_tracing(env)
    settings = OllamaSettings.from_env(env)

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        # In the background (K5-A): the service answers at once, and a question that arrives meanwhile simply waits
        # for the same load inside Ollama; the model is not loaded twice.
        app.state.warm_up_thread = threading.Thread(
            target=_warm_up_in_background, args=(warm_up, settings), name="model-warm-up", daemon=True)
        app.state.warm_up_thread.start()
        yield

    app = FastAPI(title="BankingAI AI Service", version="0.1.0", lifespan=lifespan)
    app.state.settings = settings
    app.state.warm_up = warm_up
    app.state.explainer = LangChainExplainer(settings)
    app.state.tools = McpToolGateway(McpSettings.from_env(env))
    # The address may be the default (MCP-b K5-C), so startup says which one is in use. Through uvicorn's logger: the
    # service's own logger is not configured, and uvicorn prints its INFO lines next to its own startup lines.
    logging.getLogger("uvicorn.error").info("MCP server: %s", app.state.tools.settings.url)
    app.state.chat_service = ChatService(LangChainToolSelector(settings), app.state.tools, app.state.explainer, settings,
                                         writer=LangChainClarificationWriter(settings),
                                         router=Router(LangChainIntentClassifier(settings)))
    app.state.starters = StarterQuestions(app.state.tools)

    app.add_exception_handler(RequestValidationError, _validation_failed)
    app.add_exception_handler(HTTPException, _http_error)
    app.add_exception_handler(OllamaError, _ollama_failed)
    app.add_exception_handler(AnswerRejected, _answer_rejected)
    for bridge_error in _BRIDGE_ERRORS:
        app.add_exception_handler(bridge_error, _bridge_failed)
    app.add_exception_handler(Exception, _unexpected)

    @app.get("/health/live", response_model=HealthResponse)
    def live() -> HealthResponse:
        """The process is up. Never asks Ollama, so a slow model cannot make the service look dead."""
        return HealthResponse(status="ok")

    @app.get("/health/ready", response_model=HealthResponse)
    def ready(request: Request, readiness: Readiness = Depends(get_readiness), tools_ready: bool = Depends(get_tools_ready)):
        """Ollama answers, the configured model is installed (not loaded) and the MCP server's tools can answer (K6-C)."""
        if readiness is not Readiness.READY:
            return problem(503, *_NOT_READY[readiness])
        if not tools_ready:
            return problem(503, *BACKEND_UNAVAILABLE)
        return HealthResponse(status="ready", model=request.app.state.settings.model)

    # A plain def, not async: ollama_client blocks, and FastAPI runs plain defs on a worker thread.
    @app.post("/v1/explanations", response_model=ExplanationResponse)
    def explain(
        body: ExplanationRequest, request: Request, explainer: Explainer = Depends(get_explainer)
    ) -> ExplanationResponse:
        """Explains backend facts in Turkish. The facts are text the backend formatted; nothing is computed here."""
        answer = explainer.explain(body.question, body.facts)
        return ExplanationResponse(answer=answer, model=request.app.state.settings.model)

    @app.post("/v1/chat", response_model=ChatResponse)
    def chat(
        body: ChatRequest, request: Request,
        token: str = Depends(bearer_token), service: ChatService = Depends(get_chat_service),
    ) -> ChatResponse:
        """One chat turn: the model picks a tool, the MCP server computes, the model explains (AI-14a, MCP-b)."""
        reply = service.answer(body.message, token)
        return ChatResponse(
            type=reply.type, answer=reply.answer,
            tool_calls=[ToolCallOut(tool=c.tool, arguments={k: _as_text(v) for k, v in c.arguments.items()}) for c in reply.tool_calls],
            facts=reply.facts, suggestions=reply.suggestions, model=request.app.state.settings.model)

    @app.get("/v1/suggestions", response_model=SuggestionsResponse)
    def suggestions(
        token: str = Depends(bearer_token), starters: StarterQuestions = Depends(get_starter_questions),
    ) -> SuggestionsResponse:
        """The questions to show when the chat opens, chosen from the user's data without a model (CHAT-06 K4-B)."""
        return SuggestionsResponse(suggestions=starters.for_token(token))

    return app


def _as_text(value) -> str:
    # K7-A: the reply contract (and the .NET DTO behind it) carries argument values as text; counts such as months
    # arrive as numbers (MCP-16, MCP-24) and are written the JSON way ("3", "true").
    return value if isinstance(value, str) else json.dumps(value)


def bearer_token(request: Request) -> str:
    """Dependency: the caller's bearer token; it is passed to the MCP server and never to a model or a log."""
    scheme, _, token = request.headers.get("Authorization", "").partition(" ")
    if scheme != "Bearer" or not token.strip():
        raise HTTPException(status_code=401)
    return token.strip()


def _warm_up_in_background(warm_up: Callable[[OllamaSettings], float], settings: OllamaSettings) -> None:
    log = logging.getLogger("uvicorn.error")  # next to uvicorn's startup lines, like the MCP address line
    try:
        seconds = warm_up(settings)
    except OllamaError as error:
        # Only a speed-up (K4-A): /health/ready already reports the cause, and the first question loads the model itself.
        log.warning("Model warm-up failed: %s", error)
        return
    log.info("Model %s warmed up in %.1f s; kept loaded for %s after each use", settings.model, seconds, settings.keep_alive)


def _refuse_cloud_tracing(env: Mapping[str, str]) -> None:
    for name in _TRACING_SETTINGS:
        # Anything but an explicit "off" counts as on: a typo must not let data leave the machine.
        if env.get(name, "").strip().lower() not in ("", "false", "0", "no"):
            raise ValueError(f"{name} must be off: the AI service never sends prompts to LangSmith.")


def get_chat_service(request: Request) -> ChatService:
    """Dependency: the chat service the endpoint uses (tests replace it)."""
    return request.app.state.chat_service


def get_starter_questions(request: Request) -> StarterQuestions:
    """Dependency: the starters the suggestions endpoint uses (tests replace it)."""
    return request.app.state.starters


def get_explainer(request: Request) -> Explainer:
    """Dependency: the explainer the endpoint uses (tests replace it)."""
    return request.app.state.explainer


def get_readiness(request: Request) -> Readiness:
    """Dependency: whether Ollama is up and the model installed (tests replace it)."""
    return check_ready(request.app.state.settings)


def get_tools_ready(request: Request) -> bool:
    """Dependency: whether the MCP server and its database answer (tests replace it)."""
    return request.app.state.tools.is_ready()


def problem(status: int, code: str, title: str, errors: dict[str, list[str]] | None = None,
            headers: Mapping[str, str] | None = None) -> JSONResponse:
    """A problem+json response with the same fields the .NET API uses."""
    content: dict = {"title": title, "status": status, "code": code, "traceId": uuid.uuid4().hex}
    if errors:
        content["errors"] = errors
    return JSONResponse(content, status_code=status, media_type="application/problem+json", headers=headers)


async def _validation_failed(request: Request, exc: RequestValidationError) -> JSONResponse:
    errors: dict[str, list[str]] = {}
    for error in exc.errors():
        field, message = _field_and_message(error)
        errors.setdefault(field, []).append(message)
    return problem(400, *_BY_STATUS[400], errors=errors)


async def _http_error(request: Request, exc: HTTPException) -> JSONResponse:
    code, title = _BY_STATUS.get(exc.status_code, ("error", INTERNAL_ERROR[1]))
    return problem(exc.status_code, code, title, headers=exc.headers)


async def _ollama_failed(request: Request, exc: OllamaError) -> JSONResponse:
    status, code, title = _OLLAMA_ERRORS.get(type(exc), (503, *AI_UNAVAILABLE))
    # The error text names the model and address, never the question or the facts.
    logger.warning("Ollama call failed (%s): %s", code, exc)
    return problem(status, code, title)


async def _answer_rejected(request: Request, exc: AnswerRejected) -> JSONResponse:
    # A different code from ai_bad_response on purpose: the model answered properly, the answer failed our check.
    # The message holds counts only (AnswerCheck.describe), never the answer or the numbers.
    logger.warning("Answer rejected: %s", exc)
    return problem(502, *ANSWER_REJECTED)


async def _bridge_failed(request: Request, exc: Exception) -> JSONResponse:
    status, code, title = _BRIDGE_ERRORS[type(exc)]
    # The message names the tool and the status, never the token, the question or an amount.
    logger.warning("Tool bridge failed (%s): %s", code, exc)
    return problem(status, code, title)


async def _unexpected(request: Request, exc: Exception) -> JSONResponse:
    logger.exception("Unexpected error")
    return problem(500, *INTERNAL_ERROR)


def _field_and_message(error: dict) -> tuple[str, str]:
    kind, location, context = error["type"], error["loc"], error.get("ctx", {})

    if kind == "json_invalid":
        return "body", "Geçersiz JSON."
    if kind == "missing" and tuple(location) == ("body",):
        return "body", "İstek gövdesi zorunlu."

    field = ""
    for part in location[1:] if location and location[0] == "body" else location:
        field += f"[{part}]" if isinstance(part, int) else (f".{part}" if field else str(part))

    messages = {
        "missing": "Zorunlu alan.",
        "extra_forbidden": "Tanınmayan alan.",
        "string_too_short": f"En az {context.get('min_length')} karakter olmalı.",
        "string_too_long": f"En fazla {context.get('max_length')} karakter olmalı.",
        "too_short": f"En az {context.get('min_length')} kalem olmalı.",
        "too_long": f"En fazla {context.get('max_length')} kalem olmalı.",
        # Our own rules (contracts._not_blank) already carry a Turkish message.
        "value_error": str(context.get("error", "Geçersiz değer.")),
    }
    return field or "body", messages.get(kind, "Geçersiz değer.")
