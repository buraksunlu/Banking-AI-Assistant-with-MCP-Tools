import os
from decimal import Decimal

import pytest

from chat import ChatReply, ChatService, ToolCall
from contracts import Fact
from ollama_client import OllamaSettings, Readiness, check_ready
from prompts import CLARIFY_TEXT, OUT_OF_SCOPE_TEXT
from tools import SessionInvalid

TOKEN = "secret-token-123"
SETTINGS = OllamaSettings("http://127.0.0.1:11434", "qwen3.5:4b", 30.0)
YESTERDAY = {"period": {"kind": "Yesterday", "startDate": "2026-10-09", "endDate": "2026-10-09"},
             "currencies": [{"currencyCode": "TRY", "totalAmount": Decimal("145.00"), "transactionCount": 1,
                             "uncategorizedAmount": Decimal("145.00"), "uncategorizedCount": 1}]}
YESTERDAY_FACTS = [Fact(label="Dönem", text="9 Ekim 2026"), Fact(label="Toplam harcama", text="145,00 TL"),
                   Fact(label="İşlem sayısı", text="1"), Fact(label="Sınıflandırılmamış", text="145,00 TL")]
ANSWER = "9 Ekim 2026'da toplam 145,00 TL harcadınız; bu tutar sınıflandırılmamış."


class FakeClock:
    def __init__(self):
        self.now = 0.0

    def __call__(self) -> float:
        return self.now


class FakeSelector:
    def __init__(self, clock, calls, seconds=2.0):
        self.clock, self.result, self.seconds, self.calls = clock, calls, seconds, []

    def select(self, message, deadline):
        self.calls.append((message, deadline))
        self.clock.now += self.seconds
        return self.result


class FakeGateway:
    def __init__(self, result=YESTERDAY, error=None):
        self.result, self.error, self.calls = result, error, []

    def call(self, tool, arguments, token, deadline):
        self.calls.append((tool, arguments, token, deadline))
        if self.error:
            raise self.error
        return self.result


class FakeExplainer:
    def __init__(self, answer=ANSWER):
        self.answer, self.calls = answer, []

    def explain(self, question, facts, deadline=None):
        self.calls.append((question, facts, deadline))
        return self.answer


def service(calls, gateway=None, explainer=None, seconds=2.0):
    clock = FakeClock()
    selector = FakeSelector(clock, calls, seconds)
    gateway = gateway or FakeGateway()
    explainer = explainer or FakeExplainer()
    chat = ChatService(selector, gateway, explainer, SETTINGS)
    chat.clock = clock
    return chat, selector, gateway, explainer


SUMMARY_YESTERDAY = ToolCall("get_spending_summary", {"period": "Yesterday"})


def test_a_tool_call_is_run_and_explained():
    chat, selector, gateway, explainer = service([SUMMARY_YESTERDAY])

    reply = chat.answer("Dün ne harcadım?", TOKEN)

    assert reply == ChatReply("answer", ANSWER, [SUMMARY_YESTERDAY], YESTERDAY_FACTS)
    assert gateway.calls == [("get_spending_summary", {"period": "Yesterday"}, TOKEN, 30.0)]
    assert explainer.calls == [("Dün ne harcadım?", YESTERDAY_FACTS, 30.0)]


def test_the_whole_turn_shares_one_deadline():
    # Selection took 3 s of the 30: the API call and the explanation get the same absolute deadline, not 30 s each.
    chat, selector, gateway, explainer = service([SUMMARY_YESTERDAY], seconds=3.0)
    chat.answer("Dün ne harcadım?", TOKEN)
    assert (selector.calls[0][1], gateway.calls[0][3], explainer.calls[0][2]) == (30.0, 30.0, 30.0)


def test_the_token_never_reaches_a_model():
    chat, selector, gateway, explainer = service([SUMMARY_YESTERDAY])
    chat.answer("Dün ne harcadım?", TOKEN)
    assert TOKEN not in repr(selector.calls) + repr(explainer.calls)
    assert gateway.calls[0][2] == TOKEN


def test_no_tool_means_the_fixed_out_of_scope_reply():
    chat, selector, gateway, explainer = service([])

    reply = chat.answer("Hesabımdan Deniz'e 500 TL gönder.", TOKEN)

    assert reply == ChatReply("outOfScope", OUT_OF_SCOPE_TEXT, [], [])
    assert gateway.calls == [] and explainer.calls == []


@pytest.mark.parametrize(
    "calls",
    [
        [SUMMARY_YESTERDAY, ToolCall("get_category_breakdown", {"period": "Yesterday"})],   # two at once (K2-A)
        [ToolCall("get_spending_summary", {"period": "yesterday"})],                          # not an exact name
        [ToolCall("get_merchant_spending", {"period": "ThisMonth"})],                         # merchant missing
        [ToolCall("transfer_money", {"amount": 500})],                                        # no such tool
    ],
)
def test_unusable_tool_calls_get_the_fixed_clarification(calls):
    chat, selector, gateway, explainer = service(calls)

    reply = chat.answer("Dün ne harcadım ve en çok neye?", TOKEN)

    assert (reply.type, reply.answer, reply.facts) == ("clarification", CLARIFY_TEXT, [])
    assert gateway.calls == [] and explainer.calls == []


def test_an_ambiguous_merchant_is_asked_back_without_the_explainer():
    ambiguous = {"period": {"kind": "Last3Months", "startDate": "2026-07-10", "endDate": "2026-10-10"},
                 "match": {"status": "Ambiguous", "merchant": None, "isPartialMatch": False,
                           "candidates": ["Şehir Ulaşım", "Şehirlerarası Otobüs"]}, "currencies": []}
    call = ToolCall("get_merchant_spending", {"merchant": "şehir", "period": "Last3Months"})
    chat, selector, gateway, explainer = service([call], gateway=FakeGateway(ambiguous))

    reply = chat.answer("Son 3 ayda şehir'e ne verdim?", TOKEN)

    assert reply.type == "clarification"
    assert reply.answer.endswith("Hangisini kastettiniz?")
    assert reply.tool_calls == [call]
    assert explainer.calls == []


def test_bridge_errors_are_not_hidden():
    chat, *_ = service([SUMMARY_YESTERDAY], gateway=FakeGateway(error=SessionInvalid("expired")))
    with pytest.raises(SessionInvalid):
        chat.answer("Dün ne harcadım?", TOKEN)


def _real_ready() -> bool:
    try:
        return check_ready(OllamaSettings.from_env({"BANKINGAI_LLM_MODEL": "qwen3.5:4b", **os.environ})) == Readiness.READY
    except Exception:
        return False


@pytest.mark.skipif(not _real_ready(), reason="Ollama is not running or the model is not installed")
def test_real_model_picks_yesterday():
    from chat import LangChainToolSelector

    settings = OllamaSettings.from_env({"BANKINGAI_LLM_MODEL": "qwen3.5:4b", **os.environ})
    selector = LangChainToolSelector(settings)
    assert selector.select("Dün ne harcadım?", selector.clock() + 30) == [SUMMARY_YESTERDAY]
