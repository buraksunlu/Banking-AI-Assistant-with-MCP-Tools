import re
import threading
import time
from datetime import date

import pytest
from fastapi.testclient import TestClient

from answer_check import AnswerRejected
from app import create_app, get_chat_service, get_explainer, get_readiness, get_starter_questions, get_tools_ready
from clarification import Choice
from chat import ChatReply, ChatService, LangChainClarificationWriter, LangChainToolSelector, ToolCall
from contracts import Fact
from dates import CustomerDay
from explainer import LangChainExplainer
import ollama_client
from mcp_gateway import McpToolGateway
from tools import BackendTimeout, BackendUnavailable, SessionInvalid, ToolCallRejected
from ollama_client import (
    ModelNotFound,
    OllamaBadResponse,
    OllamaTimeout,
    OllamaUnavailable,
    Readiness,
)

ENV = {"BANKINGAI_LLM_MODEL": "qwen3.5:4b"}
QUESTION = "Bu ay ne kadar harcadım?"
FACTS = [{"label": "Dönem", "text": "1-10 Ekim 2026"}, {"label": "Toplam harcama", "text": "735,00 TL"}]
ANSWER = "1-10 Ekim 2026 döneminde toplam 735,00 TL harcadınız."


class FakeExplainer:
    def __init__(self, answer: str = ANSWER, error: Exception | None = None):
        self.answer, self.error = answer, error
        self.calls: list[tuple[str, list[Fact]]] = []

    def explain(self, question: str, facts: list[Fact]) -> str:
        self.calls.append((question, facts))
        if self.error:
            raise self.error
        return self.answer


def client(explainer=None, readiness=None, tools_ready=True, raise_server_exceptions=True) -> TestClient:
    app = create_app(ENV)
    if explainer is not None:
        app.dependency_overrides[get_explainer] = lambda: explainer
    if readiness is not None:
        app.dependency_overrides[get_readiness] = readiness
    app.dependency_overrides[get_tools_ready] = lambda: tools_ready
    return TestClient(app, raise_server_exceptions=raise_server_exceptions)


def assert_problem(response, status: int, code: str, title: str) -> dict:
    assert response.status_code == status, response.text
    assert response.headers["content-type"].startswith("application/problem+json")
    body = response.json()
    assert body["status"] == status
    assert body["code"] == code
    assert body["title"] == title
    assert re.fullmatch(r"[0-9a-f]{32}", body["traceId"])
    return body


# Health


def test_live_never_asks_ollama():
    def must_not_run():
        raise AssertionError("live must not check Ollama")

    response = client(readiness=must_not_run).get("/health/live")
    assert response.status_code == 200
    assert response.json() == {"status": "ok", "model": None}


def test_ready_when_model_is_installed():
    response = client(readiness=lambda: Readiness.READY).get("/health/ready")
    assert response.status_code == 200
    assert response.json() == {"status": "ready", "model": "qwen3.5:4b"}


@pytest.mark.parametrize(
    ("readiness", "code", "title"),
    [
        (Readiness.SERVER_UNAVAILABLE, "ai_unavailable", "Asistan şu an kullanılamıyor."),
        (Readiness.MODEL_MISSING, "model_missing", "Asistan modeli bulunamadı."),
    ],
)
def test_not_ready_is_503(readiness, code, title):
    response = client(readiness=lambda: readiness).get("/health/ready")
    assert_problem(response, 503, code, title)


def test_not_ready_while_the_tools_cannot_answer():
    # MCP-b K6-C: a model without its tools cannot answer a single question, so the chat screen must not offer it.
    response = client(readiness=lambda: Readiness.READY, tools_ready=False).get("/health/ready")
    assert_problem(response, 503, "backend_unavailable", "Banka servisine şu an ulaşılamıyor.")


# Explanations: success


def test_explanation_returns_answer_and_model():
    fake = FakeExplainer()
    response = client(explainer=fake).post("/v1/explanations", json={"question": QUESTION, "facts": FACTS})
    assert response.status_code == 200
    assert response.json() == {"answer": ANSWER, "model": "qwen3.5:4b"}
    assert fake.calls == [(QUESTION, [Fact(**f) for f in FACTS])]


# Explanations: validation


def body(**changes) -> dict:
    return {"question": QUESTION, "facts": FACTS, **changes}


def fact(**changes) -> dict:
    return {"label": "Toplam harcama", "text": "735,00 TL", **changes}


@pytest.mark.parametrize(
    ("payload", "field", "message"),
    [
        (body(question=""), "question", "En az 1 karakter olmalı."),
        (body(question="   "), "question", "Boş olamaz."),
        (body(question="a" * 501), "question", "En fazla 500 karakter olmalı."),
        (body(question=123), "question", "Geçersiz değer."),
        ({"facts": FACTS}, "question", "Zorunlu alan."),
        (body(facts=[]), "facts", "En az 1 kalem olmalı."),
        (body(facts=[fact()] * 31), "facts", "En fazla 30 kalem olmalı."),
        (body(facts="735,00 TL"), "facts", "Geçersiz değer."),
        (body(facts=[fact(text="")]), "facts[0].text", "En az 1 karakter olmalı."),
        (body(facts=[fact(), fact(text=" ")]), "facts[1].text", "Boş olamaz."),
        (body(facts=[fact(text="x" * 201)]), "facts[0].text", "En fazla 200 karakter olmalı."),
        (body(facts=[fact(label="x" * 101)]), "facts[0].label", "En fazla 100 karakter olmalı."),
        (body(customerId="10000000-0000-0000-0000-000000000001"), "customerId", "Tanınmayan alan."),
        (body(facts=[fact(amount=735)]), "facts[0].amount", "Tanınmayan alan."),
    ],
)
def test_invalid_request_is_400_with_field(payload, field, message):
    fake = FakeExplainer()
    response = client(explainer=fake).post("/v1/explanations", json=payload)
    problem = assert_problem(response, 400, "validation_failed", "İstek geçersiz.")
    assert problem["errors"] == {field: [message]}
    assert fake.calls == []


def test_broken_json_is_400():
    response = client(explainer=FakeExplainer()).post(
        "/v1/explanations", content='{"question": ', headers={"Content-Type": "application/json"})
    problem = assert_problem(response, 400, "validation_failed", "İstek geçersiz.")
    assert problem["errors"] == {"body": ["Geçersiz JSON."]}


def test_missing_body_is_400():
    response = client(explainer=FakeExplainer()).post("/v1/explanations")
    problem = assert_problem(response, 400, "validation_failed", "İstek geçersiz.")
    assert problem["errors"] == {"body": ["İstek gövdesi zorunlu."]}


# Explanations: Ollama failures


@pytest.mark.parametrize(
    ("error", "status", "code", "title"),
    [
        (OllamaUnavailable("refused"), 503, "ai_unavailable", "Asistan şu an kullanılamıyor."),
        (ModelNotFound("Model 'qwen3.5:4b' is not installed"), 503, "model_missing", "Asistan modeli bulunamadı."),
        (OllamaTimeout("No answer within 30 s."), 504, "ai_timeout", "Asistan zamanında cevap veremedi."),
        (OllamaBadResponse("not JSON"), 502, "ai_bad_response", "Asistan geçersiz cevap verdi."),
    ],
)
def test_ollama_errors_map_to_problems(error, status, code, title):
    response = client(explainer=FakeExplainer(error=error)).post("/v1/explanations", json=body())
    assert_problem(response, status, code, title)


def test_rejected_answer_is_502_and_says_nothing_of_the_answer():
    fake = FakeExplainer(error=AnswerRejected("Rejected after one correction (empty: no, invented numbers: 1, forbidden mentions: 0)."))
    response = client(explainer=fake).post("/v1/explanations", json=body())
    assert_problem(response, 502, "ai_answer_rejected", "Asistan cevabı doğrulanamadı.")
    assert "invented" not in response.text


def test_unexpected_error_is_500_without_details():
    fake = FakeExplainer(error=RuntimeError("gizli ayrıntı"))
    response = client(explainer=fake, raise_server_exceptions=False).post("/v1/explanations", json=body())
    assert_problem(response, 500, "internal_error", "Beklenmeyen bir hata oluştu.")
    assert "gizli ayrıntı" not in response.text


def test_unknown_address_and_wrong_method():
    test_client = client(explainer=FakeExplainer())
    assert_problem(test_client.get("/v1/unknown"), 404, "not_found", "Bulunamadı.")
    assert_problem(test_client.get("/v1/explanations"), 405, "method_not_allowed", "Bu adres bu yöntemi desteklemiyor.")


def test_each_error_gets_its_own_trace_id():
    test_client = client(explainer=FakeExplainer())
    first = test_client.get("/v1/unknown").json()["traceId"]
    second = test_client.get("/v1/unknown").json()["traceId"]
    assert first != second


# Startup


@pytest.mark.parametrize(
    ("env", "setting"),
    [
        ({}, "BANKINGAI_LLM_MODEL"),
        ({**ENV, "BANKINGAI_LLM_TIMEOUT_SECONDS": "abc"}, "BANKINGAI_LLM_TIMEOUT_SECONDS"),
        ({**ENV, "BANKINGAI_LLM_KEEP_ALIVE": "0"}, "BANKINGAI_LLM_KEEP_ALIVE"),
        # LangSmith would send questions and facts to a cloud service; bank data must stay on this machine.
        ({**ENV, "LANGSMITH_TRACING": "true"}, "LANGSMITH_TRACING"),
        ({**ENV, "LANGCHAIN_TRACING_V2": "1"}, "LANGCHAIN_TRACING_V2"),
        ({**ENV, "BANKINGAI_MCP_URL": "localhost:5249/mcp"}, "BANKINGAI_MCP_URL"),
    ],
)
def test_service_does_not_start_with_bad_settings(env, setting):
    with pytest.raises(ValueError, match=setting):
        create_app(env)


def test_tracing_switched_off_explicitly_is_allowed():
    create_app({**ENV, "LANGSMITH_TRACING": "false", "LANGCHAIN_TRACING_V2": ""})


def test_service_explains_through_langchain():
    assert isinstance(create_app(ENV).state.explainer, LangChainExplainer)


# Warm-up (AI-15). Only `with TestClient(...)` runs the startup; the other tests never warm up.


def test_startup_warms_the_model_once_with_the_service_settings():
    calls = []
    app = create_app(ENV, warm_up=lambda settings: calls.append(settings) or 4.8)
    with TestClient(app):
        app.state.warm_up_thread.join(2)
    assert calls == [app.state.settings]


def test_startup_does_not_wait_for_the_warm_up():
    release = threading.Event()
    app = create_app(ENV, warm_up=lambda settings: release.wait(5))
    started = time.monotonic()
    with TestClient(app) as test_client:
        assert test_client.get("/health/live").status_code == 200
        assert time.monotonic() - started < 0.5
        release.set()


def test_a_failed_warm_up_is_logged_and_the_service_still_answers(caplog):
    def unavailable(settings):
        raise OllamaUnavailable("Cannot reach Ollama at http://127.0.0.1:11434: Connection refused")

    app = create_app(ENV, warm_up=unavailable)
    with TestClient(app) as test_client:
        app.state.warm_up_thread.join(2)
        assert test_client.get("/health/live").status_code == 200
    warnings = [r.getMessage() for r in caplog.records if r.levelname == "WARNING"]
    assert any("warm-up failed" in w and "Connection refused" in w for w in warnings), warnings


def test_service_warms_up_through_ollama_by_default():
    assert create_app(ENV).state.warm_up is ollama_client.warm_up


# Chat (AI-14a)


DAY = {"today": "2026-10-10", "timeZone": "Europe/Istanbul"}


class FakeChat:
    def __init__(self, reply=None, error=None):
        self.reply, self.error, self.calls, self.days = reply, error, [], []

    def answer(self, message, token, day=None):
        self.calls.append((message, token))
        self.days.append(day)
        if self.error:
            raise self.error
        return self.reply


CHAT_REPLY = ChatReply("answer", "9 Ekim 2026'da toplam 145,00 TL harcadınız.",
                       [ToolCall("get_spending_summary", {"period": "Yesterday"})],
                       [Fact(label="Dönem", text="9 Ekim 2026"), Fact(label="Toplam harcama", text="145,00 TL")])


def chat_client(fake) -> TestClient:
    app = create_app(ENV)
    app.dependency_overrides[get_chat_service] = lambda: fake
    return TestClient(app)


def test_chat_returns_the_reply_in_the_agreed_shape():
    fake = FakeChat(CHAT_REPLY)
    response = chat_client(fake).post("/v1/chat", json={"message": "Dün ne harcadım?", **DAY},
                                      headers={"Authorization": "Bearer abc"})

    assert response.status_code == 200, response.text
    assert response.json() == {
        "type": "answer",
        "answer": "9 Ekim 2026'da toplam 145,00 TL harcadınız.",
        "toolCalls": [{"tool": "get_spending_summary", "arguments": {"period": "Yesterday"}}],
        "facts": [{"label": "Dönem", "text": "9 Ekim 2026"}, {"label": "Toplam harcama", "text": "145,00 TL"}],
        "suggestions": [],
        "choices": [],
        "inputHint": "",
        "model": "qwen3.5:4b",
    }
    assert fake.calls == [("Dün ne harcadım?", "abc")]


def test_chat_passes_the_customers_day_to_the_service():
    fake = FakeChat(CHAT_REPLY)

    chat_client(fake).post("/v1/chat", json={"message": "Dün ne harcadım?", **DAY}, headers={"Authorization": "Bearer abc"})

    assert fake.days == [CustomerDay(date(2026, 10, 10), "Europe/Istanbul")]


def test_chat_returns_the_offered_follow_ups():
    reply = ChatReply("answer", "9 Ekim 2026'da toplam 145,00 TL harcadınız.", [], [], ["Kategorilere göre?", "Peki işyerlerine göre?"])

    response = chat_client(FakeChat(reply)).post("/v1/chat", json={"message": "Dün?", **DAY}, headers={"Authorization": "Bearer abc"})

    assert response.json()["suggestions"] == ["Kategorilere göre?", "Peki işyerlerine göre?"]


def test_chat_returns_a_questions_options_as_choices():
    # CHAT-10 K4-A: every choice has both fields, detail empty when the label says it alone (P5-A).
    reply = ChatReply("clarification", "Bunu iki şekilde hesaplayabilirim. Hangisini istersiniz?", [], [],
                      choices=[Choice("tek seferlik", "bir dönemdeki harcamanızla"), Choice("aylık birikim", "")])

    response = chat_client(FakeChat(reply)).post("/v1/chat", json={"message": "Dolar alsaydım?", **DAY},
                                                 headers={"Authorization": "Bearer abc"})

    assert response.json()["choices"] == [{"label": "tek seferlik", "detail": "bir dönemdeki harcamanızla"},
                                          {"label": "aylık birikim", "detail": ""}]


def test_chat_returns_an_amount_questions_input_hint():
    # PR3b D6-A: the row that opens the keyboard; empty in every other reply.
    reply = ChatReply("clarification", "Ayda kaç TL biriktirirdiniz?", [], [], choices=[Choice("1.000 TL", "")],
                      input_hint="Başka bir tutar yazın")

    response = chat_client(FakeChat(reply)).post("/v1/chat", json={"message": "Biriktirseydim?", **DAY},
                                                 headers={"Authorization": "Bearer abc"})

    assert response.json()["inputHint"] == "Başka bir tutar yazın"



def test_chat_writes_every_argument_as_text():
    # K7-A: the contract (and the .NET DTO behind it) carries argument values as text; counts arrive as numbers (MCP-16/24).
    reply = ChatReply("answer", "Temmuz 2026'da 514,98 TL harcadınız.",
                      [ToolCall("get_monthly_trend", {"months": 3}), ToolCall("some_tool", {"flag": True, "period": "ThisMonth"})], [])

    response = chat_client(FakeChat(reply)).post("/v1/chat", json={"message": "Son 3 ay?", **DAY}, headers={"Authorization": "Bearer t"})

    assert response.status_code == 200, response.text
    assert response.json()["toolCalls"] == [{"tool": "get_monthly_trend", "arguments": {"months": "3"}},
                                            {"tool": "some_tool", "arguments": {"flag": "true", "period": "ThisMonth"}}]

@pytest.mark.parametrize("header", [None, "Basic abc", "Bearer ", "Bearer   "])
def test_chat_needs_a_bearer_token(header):
    fake = FakeChat(CHAT_REPLY)
    headers = {} if header is None else {"Authorization": header}
    response = chat_client(fake).post("/v1/chat", json={"message": "Dün ne harcadım?", **DAY}, headers=headers)
    assert_problem(response, 401, "unauthorized", "Giriş gerekli.")
    assert fake.calls == []


@pytest.mark.parametrize(
    ("payload", "field", "message"),
    [
        ({"message": ""}, "message", "En az 1 karakter olmalı."),
        ({"message": "   "}, "message", "Boş olamaz."),
        ({"message": "a" * 501}, "message", "En fazla 500 karakter olmalı."),
        ({"message": "Dün?", "customerId": "1"}, "customerId", "Tanınmayan alan."),
        ({}, "message", "Zorunlu alan."),
        # CHAT-08 K9-C: the customer's day comes from the .NET API, always, and is checked like any other input.
        ({"message": "Dün?", "timeZone": "Europe/Istanbul"}, "today", "Zorunlu alan."),
        ({"message": "Dün?", "today": "2026-10-10"}, "timeZone", "Zorunlu alan."),
        ({"message": "Dün?", "today": "2026-02-31", "timeZone": "Europe/Istanbul"}, "today", "YYYY-MM-DD biçiminde geçerli bir gün olmalı."),
        ({"message": "Dün?", "today": "10.10.2026", "timeZone": "Europe/Istanbul"}, "today", "YYYY-MM-DD biçiminde geçerli bir gün olmalı."),
        ({"message": "Dün?", "today": "2026-10-10", "timeZone": "Mars/Olympus"}, "timeZone", "Tanınmayan saat dilimi."),
    ],
)
def test_chat_request_is_validated(payload, field, message):
    # The cases above that do not test the day carry a valid one.
    payload = {**({"today": "2026-10-10", "timeZone": "Europe/Istanbul"} if field not in ("today", "timeZone") else {}), **payload}
    response = chat_client(FakeChat(CHAT_REPLY)).post("/v1/chat", json=payload, headers={"Authorization": "Bearer abc"})
    problem = assert_problem(response, 400, "validation_failed", "İstek geçersiz.")
    assert problem["errors"] == {field: [message]}


@pytest.mark.parametrize(
    ("error", "status", "code", "title"),
    [
        (SessionInvalid("expired"), 401, "session_invalid", "Oturum geçersiz; yeniden giriş yapın."),
        (BackendUnavailable("down"), 503, "backend_unavailable", "Banka servisine şu an ulaşılamıyor."),
        (ToolCallRejected("400"), 502, "ai_bad_tool_call", "Asistan geçersiz bir sorgu üretti."),
        (BackendTimeout("slow"), 504, "ai_timeout", "Asistan zamanında cevap veremedi."),
    ],
)
def test_bridge_errors_map_to_problems(error, status, code, title):
    response = chat_client(FakeChat(error=error)).post("/v1/chat", json={"message": "Dün?", **DAY},
                                                       headers={"Authorization": "Bearer abc"})
    assert_problem(response, status, code, title)


def test_service_wires_the_real_chat_parts():
    service = create_app({**ENV, "BANKINGAI_MCP_URL": "http://mcp.test:9000/mcp"}).state.chat_service
    assert isinstance(service, ChatService)
    assert isinstance(service.selector, LangChainToolSelector)
    assert isinstance(service.writer, LangChainClarificationWriter)
    assert isinstance(service.gateway, McpToolGateway)
    assert service.gateway.settings.url == "http://mcp.test:9000/mcp"


# Starters (CHAT-06 K4-B)


class FakeStarters:
    def __init__(self, questions=None, error=None):
        self.questions, self.error, self.calls = questions or [], error, []

    def for_token(self, token):
        self.calls.append(token)
        if self.error:
            raise self.error
        return self.questions


def starters_client(fake) -> TestClient:
    app = create_app(ENV)
    app.dependency_overrides[get_starter_questions] = lambda: fake
    return TestClient(app)


def test_suggestions_returns_the_starters_for_the_callers_token():
    fake = FakeStarters(["Bu ay ne kadar harcadım?", "Aboneliklerim neler?"])

    response = starters_client(fake).get("/v1/suggestions", headers={"Authorization": "Bearer abc"})

    assert response.status_code == 200, response.text
    assert response.json() == {"suggestions": ["Bu ay ne kadar harcadım?", "Aboneliklerim neler?"]}
    assert fake.calls == ["abc"]


@pytest.mark.parametrize("header", [None, "Basic abc", "Bearer "])
def test_suggestions_need_a_bearer_token(header):
    fake = FakeStarters()
    headers = {} if header is None else {"Authorization": header}
    assert_problem(starters_client(fake).get("/v1/suggestions", headers=headers), 401, "unauthorized", "Giriş gerekli.")
    assert fake.calls == []


@pytest.mark.parametrize(
    ("error", "status", "code"),
    [(SessionInvalid("expired"), 401, "session_invalid"), (BackendUnavailable("down"), 503, "backend_unavailable"),
     (BackendTimeout("slow"), 504, "ai_timeout")],
)
def test_suggestion_errors_are_the_chats(error, status, code):
    response = starters_client(FakeStarters(error=error)).get("/v1/suggestions", headers={"Authorization": "Bearer abc"})
    assert (response.status_code, response.json()["code"]) == (status, code)


def test_service_wires_the_real_starters():
    from suggestions import StarterQuestions
    app = create_app(ENV)
    assert isinstance(app.state.starters, StarterQuestions) and app.state.starters.gateway is app.state.tools

