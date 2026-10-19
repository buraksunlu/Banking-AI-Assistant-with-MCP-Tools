"""FastAPI application of the AI service (AI-03).

Run (from the repository root, after loading .env):
    ai/.venv/bin/uvicorn --app-dir ai/service --factory app:create_app --host 127.0.0.1 --port 8000

The service holds no data and computes no money: it passes text the backend already formatted to the
local model. Every error is a problem+json body shaped like the .NET API's (title, status, code, traceId).
"""

import logging
import os
import uuid
from collections.abc import Mapping

from fastapi import Depends, FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from starlette.exceptions import HTTPException

from contracts import ExplanationRequest, ExplanationResponse, HealthResponse
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
)

logger = logging.getLogger("bankingai.ai")

AI_UNAVAILABLE = ("ai_unavailable", "Asistan şu an kullanılamıyor.")
MODEL_MISSING = ("model_missing", "Asistan modeli bulunamadı.")
INTERNAL_ERROR = ("internal_error", "Beklenmeyen bir hata oluştu.")

# Status → code and title, the same table as ApiProblems in the .NET API.
_BY_STATUS = {
    400: ("validation_failed", "İstek geçersiz."),
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

_NOT_READY = {Readiness.SERVER_UNAVAILABLE: AI_UNAVAILABLE, Readiness.MODEL_MISSING: MODEL_MISSING}

# LangChain sends every prompt to the LangSmith cloud when one of these is on; bank data must stay on this machine.
_TRACING_SETTINGS = ("LANGSMITH_TRACING", "LANGSMITH_TRACING_V2", "LANGCHAIN_TRACING", "LANGCHAIN_TRACING_V2")


def create_app(env: Mapping[str, str] | None = None) -> FastAPI:
    """Builds the service; fails at startup when the model setting is invalid or LangSmith tracing is on."""
    env = os.environ if env is None else env
    _refuse_cloud_tracing(env)
    settings = OllamaSettings.from_env(env)

    app = FastAPI(title="BankingAI AI Service", version="0.1.0")
    app.state.settings = settings
    app.state.explainer = LangChainExplainer(settings)

    app.add_exception_handler(RequestValidationError, _validation_failed)
    app.add_exception_handler(HTTPException, _http_error)
    app.add_exception_handler(OllamaError, _ollama_failed)
    app.add_exception_handler(Exception, _unexpected)

    @app.get("/health/live", response_model=HealthResponse)
    def live() -> HealthResponse:
        """The process is up. Never asks Ollama, so a slow model cannot make the service look dead."""
        return HealthResponse(status="ok")

    @app.get("/health/ready", response_model=HealthResponse)
    def ready(request: Request, readiness: Readiness = Depends(get_readiness)):
        """Ollama answers and the configured model is installed (the model is not loaded)."""
        if readiness is Readiness.READY:
            return HealthResponse(status="ready", model=request.app.state.settings.model)
        return problem(503, *_NOT_READY[readiness])

    # A plain def, not async: ollama_client blocks, and FastAPI runs plain defs on a worker thread.
    @app.post("/v1/explanations", response_model=ExplanationResponse)
    def explain(
        body: ExplanationRequest, request: Request, explainer: Explainer = Depends(get_explainer)
    ) -> ExplanationResponse:
        """Explains backend facts in Turkish. The facts are text the backend formatted; nothing is computed here."""
        answer = explainer.explain(body.question, body.facts)
        return ExplanationResponse(answer=answer, model=request.app.state.settings.model)

    return app


def _refuse_cloud_tracing(env: Mapping[str, str]) -> None:
    for name in _TRACING_SETTINGS:
        # Anything but an explicit "off" counts as on: a typo must not let data leave the machine.
        if env.get(name, "").strip().lower() not in ("", "false", "0", "no"):
            raise ValueError(f"{name} must be off: the AI service never sends prompts to LangSmith.")


def get_explainer(request: Request) -> Explainer:
    """Dependency: the explainer the endpoint uses (tests replace it)."""
    return request.app.state.explainer


def get_readiness(request: Request) -> Readiness:
    """Dependency: whether Ollama is up and the model installed (tests replace it)."""
    return check_ready(request.app.state.settings)


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
