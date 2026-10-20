import os
from decimal import Decimal

import pytest

from chat import ChatReply, ChatService, ToolCall
from contracts import Fact
from ollama_client import OllamaSettings, Readiness, check_ready
from prompts import CLARIFY_TEXT, OUT_OF_SCOPE_TEXT
from tools import BackendUnavailable, SessionInvalid, ToolArgumentsRejected

TOKEN = "secret-token-123"
SETTINGS = OllamaSettings("http://127.0.0.1:11434", "qwen3.5:4b", 30.0)
YESTERDAY = {"period": {"kind": "Yesterday", "startDate": "2026-10-09", "endDate": "2026-10-09"},
             "currencies": [{"currencyCode": "TRY", "totalAmount": Decimal("145.00"), "transactionCount": 1,
                             "uncategorizedAmount": Decimal("145.00"), "uncategorizedCount": 1}]}
YESTERDAY_FACTS = [Fact(label="Dönem", text="9 Ekim 2026"), Fact(label="Toplam harcama", text="145,00 TL"),
                   Fact(label="İşlem sayısı", text="1"), Fact(label="Sınıflandırılmamış", text="145,00 TL")]
ANSWER = "9 Ekim 2026'da toplam 145,00 TL harcadınız; bu tutar sınıflandırılmamış."

# The tools as the .NET MCP server publishes them (tools/list, 20 Oct 2026), in the model's tool shape.
PERIOD_HELP = ("period: Today=bugün, Yesterday=dün, ThisWeek=bu hafta (pazartesiden bugüne), LastWeek=geçen hafta, "
               "Last7Days=son 7 gün, Last30Days=son 30 gün, ThisMonth=bu ay, LastMonth=geçen ay, Last3Months=son 3 ay, "
               "Last6Months=son 6 ay, ThisYear=bu yıl.")
PERIOD = {"type": "string", "enum": ["Today", "Yesterday", "ThisWeek", "LastWeek", "Last7Days", "Last30Days",
                                     "ThisMonth", "LastMonth", "Last3Months", "Last6Months", "ThisYear"]}
DEFINITIONS = [
    {"type": "function", "function": {
        "name": "get_spending_summary", "description": "Bir dönemdeki toplam harcamayı para birimi başına döndürür. " + PERIOD_HELP,
        "parameters": {"type": "object", "properties": {"period": PERIOD}, "required": ["period"]}}},
    {"type": "function", "function": {
        "name": "get_category_breakdown", "description": "Bir dönemdeki harcamanın kategorilere dağılımını döndürür. " + PERIOD_HELP,
        "parameters": {"type": "object", "properties": {"period": PERIOD}, "required": ["period"]}}},
    {"type": "function", "function": {
        "name": "get_merchant_spending",
        "description": "Bir dönemde tek bir işyerine (merchant) yapılan harcamayı döndürür. merchant: kullanıcının söylediği işyeri adı. "
                       + PERIOD_HELP,
        "parameters": {"type": "object", "properties": {
            "merchant": {"type": "string", "minLength": 1, "maxLength": 150}, "period": PERIOD}, "required": ["merchant", "period"]}}},
]


class FakeClock:
    def __init__(self):
        self.now = 0.0

    def __call__(self) -> float:
        return self.now


class FakeSelector:
    def __init__(self, clock, calls, seconds=2.0):
        self.clock, self.result, self.seconds, self.calls = clock, calls, seconds, []

    def select(self, message, tools, deadline):
        self.calls.append((message, tools, deadline))
        self.clock.now += self.seconds
        return self.result


class FakeGateway:
    def __init__(self, result=YESTERDAY, error=None, list_error=None):
        self.result, self.error, self.list_error = result, error, list_error
        self.calls, self.list_calls = [], []

    def list_tools(self, token, deadline):
        self.list_calls.append((token, deadline))
        if self.list_error:
            raise self.list_error
        return DEFINITIONS

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
    assert gateway.list_calls == [(TOKEN, 30.0)]
    assert selector.calls == [("Dün ne harcadım?", DEFINITIONS, 30.0)]
    assert gateway.calls == [("get_spending_summary", {"period": "Yesterday"}, TOKEN, 30.0)]
    assert explainer.calls == [("Dün ne harcadım?", YESTERDAY_FACTS, 30.0)]


def test_the_whole_turn_shares_one_deadline():
    # Selection took 3 s of the 30: listing, the tool call and the explanation get the same absolute deadline, not 30 s each.
    chat, selector, gateway, explainer = service([SUMMARY_YESTERDAY], seconds=3.0)
    chat.answer("Dün ne harcadım?", TOKEN)
    assert (gateway.list_calls[0][1], selector.calls[0][2], gateway.calls[0][3], explainer.calls[0][2]) == (30.0, 30.0, 30.0, 30.0)


def test_the_token_never_reaches_a_model():
    chat, selector, gateway, explainer = service([SUMMARY_YESTERDAY])
    chat.answer("Dün ne harcadım?", TOKEN)
    assert TOKEN not in repr(selector.calls) + repr(explainer.calls)
    assert gateway.calls[0][2] == TOKEN and gateway.list_calls[0][0] == TOKEN


def test_no_tool_means_the_fixed_out_of_scope_reply():
    chat, selector, gateway, explainer = service([])

    reply = chat.answer("Hesabımdan Deniz'e 500 TL gönder.", TOKEN)

    assert reply == ChatReply("outOfScope", OUT_OF_SCOPE_TEXT, [], [])
    assert gateway.calls == [] and explainer.calls == []


@pytest.mark.parametrize(
    "calls",
    [
        [SUMMARY_YESTERDAY, ToolCall("get_category_breakdown", {"period": "Yesterday"})],   # two at once (K2-A)
        [ToolCall("transfer_money", {"amount": 500})],                                        # not a published tool
    ],
)
def test_unusable_tool_calls_get_the_fixed_clarification(calls):
    chat, selector, gateway, explainer = service(calls)

    reply = chat.answer("Dün ne harcadım ve en çok neye?", TOKEN)

    assert (reply.type, reply.answer, reply.facts) == ("clarification", CLARIFY_TEXT, [])
    assert gateway.calls == [] and explainer.calls == []


@pytest.mark.parametrize(
    "call",
    [
        ToolCall("get_spending_summary", {"period": "yesterday"}),   # not an exact name
        ToolCall("get_merchant_spending", {"period": "ThisMonth"}),  # merchant missing
    ],
)
def test_arguments_the_server_refuses_get_the_fixed_clarification(call):
    # The MCP server checks arguments against the schema it published (MCP-b K3-A); the user sees what they saw before.
    gateway = FakeGateway(error=ToolArgumentsRejected("invalid_arguments"))
    chat, selector, gateway, explainer = service([call], gateway=gateway)

    reply = chat.answer("Dün ne harcadım?", TOKEN)

    assert (reply.type, reply.answer, reply.facts, reply.tool_calls) == ("clarification", CLARIFY_TEXT, [], [call])
    assert len(gateway.calls) == 1 and explainer.calls == []


def test_an_unreachable_tool_server_stops_the_turn_before_the_model_runs():
    chat, selector, gateway, explainer = service([SUMMARY_YESTERDAY], gateway=FakeGateway(list_error=BackendUnavailable("down")))

    with pytest.raises(BackendUnavailable):
        chat.answer("Dün ne harcadım?", TOKEN)

    # No seconds of the single local model spent on a turn that cannot be answered.
    assert selector.calls == [] and explainer.calls == []


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
def test_the_selector_keeps_the_model_loaded():
    from chat import LangChainToolSelector
    from test_explainer import FakeOllamaStream, chunk, final

    with FakeOllamaStream([chunk("Merhaba"), final()]) as fake:
        selector = LangChainToolSelector(OllamaSettings(fake.url, "qwen3.5:4b", 5.0))
        assert selector.select("Merhaba", DEFINITIONS, selector.clock() + 5.0) == []
    assert fake.requests[0][1]["keep_alive"] == "30m"


def test_the_selector_caps_its_answer():
    # K8-A: a tool call is 30-60 tokens; a model writing a joke instead must not run into the turn's 30 s.
    from chat import MAX_TOOL_CALL_TOKENS, LangChainToolSelector
    from test_explainer import FakeOllamaStream, chunk, final

    with FakeOllamaStream([chunk("Merhaba"), final()]) as fake:
        selector = LangChainToolSelector(OllamaSettings(fake.url, "qwen3.5:4b", 5.0))
        selector.select("Bana bir fıkra anlat", DEFINITIONS, selector.clock() + 5.0)
    assert fake.requests[0][1]["options"]["num_predict"] == MAX_TOOL_CALL_TOKENS == 200


def test_a_text_answer_cut_at_the_cap_means_no_tool():
    from chat import LangChainToolSelector
    from test_explainer import FakeOllamaStream, chunk, final

    with FakeOllamaStream([chunk("Bir gün Temel ile Dursun "), final(done_reason="length")]) as fake:
        selector = LangChainToolSelector(OllamaSettings(fake.url, "qwen3.5:4b", 5.0))
        assert selector.select("Bana bir fıkra anlat", DEFINITIONS, selector.clock() + 5.0) == []


def test_real_model_picks_yesterday():
    from chat import LangChainToolSelector

    settings = OllamaSettings.from_env({"BANKINGAI_LLM_MODEL": "qwen3.5:4b", **os.environ})
    selector = LangChainToolSelector(settings)
    assert selector.select("Dün ne harcadım?", DEFINITIONS, selector.clock() + 30) == [SUMMARY_YESTERDAY]
