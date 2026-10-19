import re

import pytest
from fastapi.testclient import TestClient

from answer_check import AnswerRejected
from app import create_app, get_chat_service, get_explainer, get_readiness
from chat import ChatReply, ChatService, LangChainToolSelector, ToolCall
from contracts import Fact
from explainer import LangChainExplainer
from tools import BackendTimeout, BackendUnavailable, RestToolGateway, SessionInvalid, ToolCallRejected
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


def client(explainer=None, readiness=None, raise_server_exceptions=True) -> TestClient:
    app = create_app(ENV)
    if explainer is not None:
        app.dependency_overrides[get_explainer] = lambda: explainer
    if readiness is not None:
        app.dependency_overrides[get_readiness] = readiness
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
        # LangSmith would send questions and facts to a cloud service; bank data must stay on this machine.
        ({**ENV, "LANGSMITH_TRACING": "true"}, "LANGSMITH_TRACING"),
        ({**ENV, "LANGCHAIN_TRACING_V2": "1"}, "LANGCHAIN_TRACING_V2"),
    ],
)
def test_service_does_not_start_with_bad_settings(env, setting):
    with pytest.raises(ValueError, match=setting):
        create_app(env)


def test_tracing_switched_off_explicitly_is_allowed():
    create_app({**ENV, "LANGSMITH_TRACING": "false", "LANGCHAIN_TRACING_V2": ""})


def test_service_explains_through_langchain():
    assert isinstance(create_app(ENV).state.explainer, LangChainExplainer)


# Chat (AI-14a)


class FakeChat:
    def __init__(self, reply=None, error=None):
        self.reply, self.error, self.calls = reply, error, []

    def answer(self, message, token):
        self.calls.append((message, token))
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
    response = chat_client(fake).post("/v1/chat", json={"message": "Dün ne harcadım?"},
                                      headers={"Authorization": "Bearer abc"})

    assert response.status_code == 200, response.text
    assert response.json() == {
        "type": "answer",
        "answer": "9 Ekim 2026'da toplam 145,00 TL harcadınız.",
        "toolCalls": [{"tool": "get_spending_summary", "arguments": {"period": "Yesterday"}}],
        "facts": [{"label": "Dönem", "text": "9 Ekim 2026"}, {"label": "Toplam harcama", "text": "145,00 TL"}],
        "model": "qwen3.5:4b",
    }
    assert fake.calls == [("Dün ne harcadım?", "abc")]


@pytest.mark.parametrize("header", [None, "Basic abc", "Bearer ", "Bearer   "])
def test_chat_needs_a_bearer_token(header):
    fake = FakeChat(CHAT_REPLY)
    headers = {} if header is None else {"Authorization": header}
    response = chat_client(fake).post("/v1/chat", json={"message": "Dün ne harcadım?"}, headers=headers)
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
    ],
)
def test_chat_request_is_validated(payload, field, message):
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
    response = chat_client(FakeChat(error=error)).post("/v1/chat", json={"message": "Dün?"},
                                                       headers={"Authorization": "Bearer abc"})
    assert_problem(response, status, code, title)


def test_service_wires_the_real_chat_parts():
    service = create_app({**ENV, "BANKINGAI_API_URL": "http://api.test:8080/"}).state.chat_service
    assert isinstance(service, ChatService)
    assert isinstance(service.selector, LangChainToolSelector)
    assert isinstance(service.gateway, RestToolGateway)
    assert service.gateway.settings.base_url == "http://api.test:8080"
