import os
from decimal import Decimal

import pytest

from chat import ChatReply, ChatService, ToolCall
from contracts import Fact
from ollama_client import OllamaSettings, OllamaTimeout, OllamaUnavailable, Readiness, check_ready
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


# MCP-16/24 as published: a required and an optional count.
DEFINITIONS += [
    {"type": "function", "function": {
        "name": "get_monthly_trend", "description": "Son birkaç ayın harcamasını takvim ayı olarak döndürür.",
        "parameters": {"type": "object", "properties": {"months": {"type": "integer", "minimum": 1, "maximum": 12}},
                       "required": ["months"]}}},
    {"type": "function", "function": {
        "name": "get_upcoming_payments", "description": "Önümüzdeki günlerde beklenen ödemeleri döndürür.",
        "parameters": {"type": "object", "properties": {"days": {"type": "integer", "minimum": 1, "maximum": 31, "default": 30}}}}},
]


class FakeClock:
    def __init__(self):
        self.now = 0.0

    def __call__(self) -> float:
        return self.now


class FakeSelector:
    def __init__(self, clock, calls, seconds=2.0, turns=None):
        self.clock, self.result, self.seconds, self.turns, self.calls = clock, calls, seconds, turns, []
        self.contexts = []

    def select(self, message, tools, deadline, context=None):
        self.calls.append((message, tools, deadline))
        self.contexts.append(context)
        self.clock.now += self.seconds
        # ``turns`` answers turn by turn (a question and its follow-ups); otherwise every turn gets the same calls.
        return self.turns.pop(0) if self.turns else self.result


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
        # A list answers call by call (a clarification followed by the completed call).
        return self.result.pop(0) if isinstance(self.result, list) else self.result


class FakeExplainer:
    def __init__(self, answer=ANSWER):
        self.answer, self.calls = answer, []

    def explain(self, question, facts, deadline=None):
        self.calls.append((question, facts, deadline))
        return self.answer


def service(calls, gateway=None, explainer=None, seconds=2.0, writer=None, turns=None):
    clock = FakeClock()
    selector = FakeSelector(clock, calls, seconds, turns)
    gateway = gateway or FakeGateway()
    explainer = explainer or FakeExplainer()
    chat = ChatService(selector, gateway, explainer, SETTINGS, writer=writer)
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


# CHAT-03: guessed arguments are asked back, and the reply completes the stored question.

AMBIGUOUS_CITY = {"period": {"kind": "Last6Months", "startDate": "2026-04-10", "endDate": "2026-10-10"},
                  "match": {"status": "Ambiguous", "merchant": None, "isPartialMatch": False,
                            "candidates": ["Şehir Ulaşım", "Şehirlerarası Otobüs"]}, "currencies": []}
CITY_SIX_MONTHS = {"period": {"kind": "Last6Months", "startDate": "2026-04-10", "endDate": "2026-10-10"},
                   "match": {"status": "Matched", "merchant": "Şehir Ulaşım", "isPartialMatch": False, "candidates": []},
                   "currencies": [{"currencyCode": "TRY", "totalAmount": Decimal("5600.00"), "transactionCount": 13}]}
LAST_MONTH = {"period": {"kind": "LastMonth", "startDate": "2026-09-01", "endDate": "2026-09-30"},
              "currencies": [{"currencyCode": "TRY", "totalAmount": Decimal("624.98"), "transactionCount": 4,
                              "uncategorizedAmount": Decimal("0.00"), "uncategorizedCount": 0}]}
PERIOD_QUESTION = "Hangi dönem için? 1) bu ay 2) geçen ay 3) son 3 ay"


class FakeWriter:
    def __init__(self, text=None, error=None):
        self.text, self.error, self.calls = text, error, []

    def write(self, question, kind, options, numbered, deadline, asked=""):
        self.calls.append((question, kind, options, numbered, deadline, asked))
        if self.error:
            raise self.error
        return self.text


def test_a_guessed_period_is_asked_back_and_nothing_runs():
    chat, selector, gateway, explainer = service([ToolCall("get_spending_summary", {"period": "ThisMonth"})])

    reply = chat.answer("Ne kadar harcadım?", TOKEN)

    assert reply == ChatReply("clarification", PERIOD_QUESTION, [], [])
    assert gateway.calls == [] and explainer.calls == []


def test_the_reply_completes_the_question_without_the_model():
    gateway = FakeGateway(LAST_MONTH)
    chat, selector, gateway, explainer = service([ToolCall("get_spending_summary", {"period": "ThisMonth"})], gateway=gateway)
    chat.answer("Ne kadar harcadım?", TOKEN)

    reply = chat.answer("geçen ay", TOKEN)

    called = ToolCall("get_spending_summary", {"period": "LastMonth"})
    assert (reply.type, reply.tool_calls) == ("answer", [called])
    # No second selection and no second listing: the stored question already names the tool.
    assert len(selector.calls) == 1 and len(gateway.list_calls) == 1
    # The reply's turn has its own deadline: 2 s of the first turn passed on the fake clock, so 32 s.
    assert gateway.calls == [("get_spending_summary", {"period": "LastMonth"}, TOKEN, 32.0)]
    assert explainer.calls[0][0] == "Ne kadar harcadım? (geçen ay)"
    assert ("Toplam harcama", "624,98 TL") in [(f.label, f.text) for f in explainer.calls[0][1]]


def test_a_merchant_choice_keeps_the_period_of_the_question():
    gateway = FakeGateway([AMBIGUOUS_CITY, CITY_SIX_MONTHS])
    call = ToolCall("get_merchant_spending", {"merchant": "şehir", "period": "Last6Months"})
    chat, selector, gateway, explainer = service([call], gateway=gateway)

    asked = chat.answer("Son 6 ayda şehir'e ne kadar harcadım?", TOKEN)
    reply = chat.answer("1", TOKEN)

    assert asked.answer == "‘şehir’ birden fazla işyeriyle eşleşti: 1) Şehir Ulaşım 2) Şehirlerarası Otobüs. Hangisini kastettiniz?"
    assert gateway.calls[1][:2] == ("get_merchant_spending", {"period": "Last6Months", "merchant": "Şehir Ulaşım"})
    assert reply.type == "answer"
    assert ("Toplam harcama", "5.600,00 TL") in [(f.label, f.text) for f in explainer.calls[0][1]]


def test_a_guessed_count_is_asked_and_the_number_completes_it():
    window = {"year": 2026, "month": 10, "isPartial": True,
              "period": {"kind": "Custom", "startDate": "2026-10-01", "endDate": "2026-10-10"}}
    trend = {"months": [window], "merchantMatch": None, "currencies": []}
    gateway = FakeGateway(trend)
    chat, selector, gateway, explainer = service([ToolCall("get_monthly_trend", {"months": 6})], gateway=gateway)

    asked = chat.answer("Harcamam aylara göre nasıl değişti?", TOKEN)
    chat.answer("6", TOKEN)

    assert asked.answer == "Kaç aylık bakayım? Örneğin son 3, 6 ya da 12 ay."
    assert gateway.calls[0][:2] == ("get_monthly_trend", {"months": 6})


def test_a_guessed_optional_count_is_dropped_and_the_tool_runs():
    upcoming = {"from": "2026-10-10", "to": "2026-11-09", "payments": [], "totals": []}
    chat, selector, gateway, explainer = service([ToolCall("get_upcoming_payments", {"days": 30})], gateway=FakeGateway(upcoming))

    reply = chat.answer("Yaklaşan ödemelerim neler?", TOKEN)

    assert gateway.calls[0][:2] == ("get_upcoming_payments", {})
    assert reply.tool_calls == [ToolCall("get_upcoming_payments", {})]


def test_another_question_instead_of_a_choice_is_answered_and_the_old_one_dropped():
    chat, selector, gateway, explainer = service([ToolCall("get_spending_summary", {"period": "ThisMonth"})])
    chat.answer("Ne kadar harcadım?", TOKEN)

    chat.answer("Bu hafta Starbucks'a ne kadar verdim?", TOKEN)
    chat.answer("geçen ay", TOKEN)

    # The second message names a period but is a question of its own (K5-A: too long for a reply), so the old question is
    # dropped and the third message finds nothing waiting either.
    assert [c[0] for c in selector.calls] == ["Ne kadar harcadım?", "Bu hafta Starbucks'a ne kadar verdim?", "geçen ay"]


def test_a_waiting_question_belongs_to_its_token_only():
    chat, selector, gateway, explainer = service([ToolCall("get_spending_summary", {"period": "ThisMonth"})])
    chat.answer("Ne kadar harcadım?", TOKEN)

    chat.answer("geçen ay", "another-users-token")

    assert [c[0] for c in selector.calls] == ["Ne kadar harcadım?", "geçen ay"]


def test_a_written_question_is_shown_when_it_names_exactly_the_options():
    writer = FakeWriter("Hangi döneme ait harcamaları öğrenmek istiyorsunuz?\n1. Bu ay\n2. Geçen ay\n3. Son 3 ay")
    chat, *_ = service([ToolCall("get_spending_summary", {"period": "ThisMonth"})], writer=writer)

    reply = chat.answer("Ne kadar harcadım?", TOKEN)

    assert reply.answer == writer.text
    # At most 8 s for the wording (2 s of selection passed): the fixed question is better than a long wait.
    assert writer.calls == [("Ne kadar harcadım?", "period", ["bu ay", "geçen ay", "son 3 ay"], True, 10.0, "")]


@pytest.mark.parametrize("writer", [
    FakeWriter("Hangi dönem? 1) bu ay 2) geçen ay"),                              # an option left out
    FakeWriter("Bu ay 735,00 TL harcadınız; başka dönem? 1) bu ay 2) geçen ay 3) son 3 ay"),  # an invented figure
    FakeWriter(error=OllamaTimeout("slow")),
    FakeWriter(error=OllamaUnavailable("down")),
])
def test_otherwise_the_fixed_question_is_shown(writer):
    chat, *_ = service([ToolCall("get_spending_summary", {"period": "ThisMonth"})], writer=writer)

    reply = chat.answer("Ne kadar harcadım?", TOKEN)

    assert (reply.type, reply.answer) == ("clarification", PERIOD_QUESTION)


def test_the_choice_is_matched_against_the_stored_options_whatever_the_model_wrote():
    writer = FakeWriter("Hangi işyerini kastettiniz? 1. Şehir Ulaşım 2. Şehirlerarası Otobüs")
    gateway = FakeGateway([AMBIGUOUS_CITY, CITY_SIX_MONTHS])
    call = ToolCall("get_merchant_spending", {"merchant": "şehir", "period": "Last6Months"})
    chat, selector, gateway, explainer = service([call], gateway=gateway, writer=writer)

    chat.answer("Son 6 ayda şehir'e ne kadar harcadım?", TOKEN)
    chat.answer("ikincisi", TOKEN)

    assert writer.calls[0][5] == "şehir"  # the model is told which words matched several merchants
    assert gateway.calls[1][1] == {"period": "Last6Months", "merchant": "Şehirlerarası Otobüs"}


def test_the_writer_asks_the_model_for_one_short_question():
    from chat import MAX_QUESTION_TOKENS, LangChainClarificationWriter
    from prompts import build_clarify_messages
    from test_explainer import FakeOllamaStream, chunk, final

    with FakeOllamaStream([chunk("Hangi dönem? "), chunk("1) bu ay 2) geçen ay 3) son 3 ay\n"), final()]) as fake:
        writer = LangChainClarificationWriter(OllamaSettings(fake.url, "qwen3.5:4b", 5.0))
        text = writer.write("Ne kadar harcadım?", "period", ["bu ay", "geçen ay", "son 3 ay"], True, writer.clock() + 5.0)

    assert text == "Hangi dönem? 1) bu ay 2) geçen ay 3) son 3 ay"
    body = fake.requests[0][1]
    assert (body["options"]["num_predict"], body["keep_alive"], body["think"]) == (MAX_QUESTION_TOKENS, "30m", False)
    expected = build_clarify_messages("Ne kadar harcadım?", "period", ["bu ay", "geçen ay", "son 3 ay"], numbered=True)
    assert [{"role": m["role"], "content": m["content"]} for m in body["messages"]] == expected


# CHAT-02: the last successful call is the context of a short follow-up.

THIS_MONTH_SUMMARY = {"period": {"kind": "ThisMonth", "startDate": "2026-10-01", "endDate": "2026-10-10"},
                      "currencies": [{"currencyCode": "TRY", "totalAmount": Decimal("735.00"), "transactionCount": 5,
                                      "uncategorizedAmount": Decimal("145.00"), "uncategorizedCount": 1}]}


def merchant_result(name, kind, start, end, amount, count):
    return {"period": {"kind": kind, "startDate": start, "endDate": end},
            "match": {"status": "Matched", "merchant": name, "isPartialMatch": False, "candidates": []},
            "currencies": [{"currencyCode": "TRY", "totalAmount": Decimal(amount), "transactionCount": count}]}


STARBUCKS_LAST_MONTH = ToolCall("get_merchant_spending", {"merchant": "Starbucks", "period": "LastMonth"})
KAHVE_LAST_MONTH = ToolCall("get_merchant_spending", {"merchant": "Kahve Durağı", "period": "LastMonth"})


def test_a_follow_up_gets_the_last_call_and_changes_only_what_was_said():
    gateway = FakeGateway([THIS_MONTH_SUMMARY, LAST_MONTH])
    turns = [[ToolCall("get_spending_summary", {"period": "ThisMonth"})], [ToolCall("get_spending_summary", {"period": "LastMonth"})]]
    chat, selector, gateway, explainer = service([], gateway=gateway, turns=turns)

    chat.answer("Bu ay ne kadar harcadım?", TOKEN)
    reply = chat.answer("Peki geçen ay?", TOKEN)

    assert selector.contexts[0] is None
    assert (selector.contexts[1].tool, selector.contexts[1].arguments, selector.contexts[1].question) == (
        "get_spending_summary", {"period": "ThisMonth"}, "Bu ay ne kadar harcadım?")
    assert gateway.calls[1][:2] == ("get_spending_summary", {"period": "LastMonth"})
    assert reply.tool_calls == [ToolCall("get_spending_summary", {"period": "LastMonth"})]
    # Only the new message is explained: the live trial's "Peki bu ay?" was answered "Geçen ay (1-10 Ekim) …" when the
    # previous question went along; the period is in the facts anyway.
    assert explainer.calls[1][0] == "Peki geçen ay?"
    # Only the new result is explained: the previous answer's 735,00 TL is not a fact of this turn.
    assert all("735" not in f.text for f in explainer.calls[1][1])


def test_the_period_of_the_previous_call_is_kept_for_another_merchant():
    gateway = FakeGateway([merchant_result("Starbucks", "LastMonth", "2026-09-01", "2026-09-30", "185.00", 1),
                           merchant_result("Kahve Durağı", "LastMonth", "2026-09-01", "2026-09-30", "110.00", 1)])
    chat, selector, gateway, explainer = service([], gateway=gateway, turns=[[STARBUCKS_LAST_MONTH], [KAHVE_LAST_MONTH]])

    chat.answer("Geçen ay Starbucks'a ne kadar harcadım?", TOKEN)
    reply = chat.answer("Peki Kahve Durağı'na?", TOKEN)

    assert reply.type == "answer"
    assert gateway.calls[1][1] == {"merchant": "Kahve Durağı", "period": "LastMonth"}


def test_a_chain_of_follow_ups_keeps_only_the_last_message_and_merchant():
    gateway = FakeGateway([merchant_result("Starbucks", "LastMonth", "2026-09-01", "2026-09-30", "185.00", 1),
                           merchant_result("Kahve Durağı", "LastMonth", "2026-09-01", "2026-09-30", "110.00", 1),
                           merchant_result("Kahve Durağı", "Last3Months", "2026-07-10", "2026-10-10", "325.00", 3)])
    # The model falls back to the first merchant, as it did in the live trial.
    back_to_starbucks = ToolCall("get_merchant_spending", {"merchant": "Starbucks", "period": "Last3Months"})
    chat, selector, gateway, explainer = service([], gateway=gateway, turns=[[STARBUCKS_LAST_MONTH], [KAHVE_LAST_MONTH], [back_to_starbucks]])

    chat.answer("Geçen ay Starbucks'a ne kadar harcadım?", TOKEN)
    chat.answer("Peki Kahve Durağı'na?", TOKEN)
    reply = chat.answer("Ya son 3 ayda?", TOKEN)

    assert selector.contexts[2].question == "Peki Kahve Durağı'na?"  # no growing chain with the first merchant in it
    assert gateway.calls[2][1] == {"merchant": "Kahve Durağı", "period": "Last3Months"}
    assert reply.tool_calls == [ToolCall("get_merchant_spending", {"merchant": "Kahve Durağı", "period": "Last3Months"})]


def test_an_argument_neither_said_nor_carried_over_is_asked_back():
    invented = ToolCall("get_merchant_spending", {"merchant": "Kahve Durağı", "period": "ThisYear"})
    gateway = FakeGateway([merchant_result("Starbucks", "LastMonth", "2026-09-01", "2026-09-30", "185.00", 1)])
    chat, selector, gateway, explainer = service([], gateway=gateway, turns=[[STARBUCKS_LAST_MONTH], [invented]])

    chat.answer("Geçen ay Starbucks'a ne kadar harcadım?", TOKEN)
    reply = chat.answer("Peki Kahve Durağı'na?", TOKEN)

    assert (reply.type, reply.answer) == ("clarification", PERIOD_QUESTION)
    assert len(gateway.calls) == 1


def test_a_full_question_gets_no_context():
    gateway = FakeGateway([THIS_MONTH_SUMMARY, LAST_MONTH])
    turns = [[ToolCall("get_spending_summary", {"period": "ThisMonth"})], [ToolCall("get_spending_summary", {"period": "LastMonth"})]]
    chat, selector, gateway, explainer = service([], gateway=gateway, turns=turns)

    chat.answer("Bu ay ne kadar harcadım?", TOKEN)
    chat.answer("Geçen ay toplam ne kadar harcadım?", TOKEN)

    assert selector.contexts == [None, None]
    assert explainer.calls[1][0] == "Geçen ay toplam ne kadar harcadım?"


def test_the_context_belongs_to_its_token_only():
    chat, selector, gateway, explainer = service([ToolCall("get_spending_summary", {"period": "ThisMonth"})],
                                                 gateway=FakeGateway(THIS_MONTH_SUMMARY))
    chat.answer("Bu ay ne kadar harcadım?", TOKEN)

    chat.answer("Peki geçen ay?", "another-users-token")

    assert selector.contexts[1] is None


def test_a_turn_without_a_tool_keeps_the_context_for_the_next_follow_up():
    gateway = FakeGateway([THIS_MONTH_SUMMARY, LAST_MONTH])
    turns = [[ToolCall("get_spending_summary", {"period": "ThisMonth"})], [],
             [ToolCall("get_spending_summary", {"period": "LastMonth"})]]
    chat, selector, gateway, explainer = service([], gateway=gateway, turns=turns)

    chat.answer("Bu ay ne kadar harcadım?", TOKEN)
    assert chat.answer("Peki ya?", TOKEN).type == "outOfScope"
    chat.answer("Peki geçen ay?", TOKEN)

    assert selector.contexts[2].arguments == {"period": "ThisMonth"}


def test_a_call_completed_by_a_clarification_becomes_the_context():
    gateway = FakeGateway([LAST_MONTH, THIS_MONTH_SUMMARY])
    turns = [[ToolCall("get_spending_summary", {"period": "ThisMonth"})], [ToolCall("get_spending_summary", {"period": "ThisMonth"})]]
    chat, selector, gateway, explainer = service([], gateway=gateway, turns=turns)

    chat.answer("Ne kadar harcadım?", TOKEN)    # asked back (CHAT-03)
    chat.answer("geçen ay", TOKEN)              # completes the call without the selector
    chat.answer("Peki bu ay?", TOKEN)

    assert selector.contexts[1].arguments == {"period": "LastMonth"}
    assert selector.contexts[1].question == "Ne kadar harcadım? (geçen ay)"
    assert explainer.calls[1][0] == "Peki bu ay?"
    assert gateway.calls[1][1] == {"period": "ThisMonth"}


# MCP-17/18: the user's own figures. The tools as the MCP server publishes them (tools/list, 21 Oct 2026), shortened.

CATEGORY = {"type": ["string", "null"], "enum": ["FOOD", "GROCERY", "OTHER", None], "default": None}
DEFINITIONS += [
    {"type": "function", "function": {
        "name": "check_budget", "description": "Bu ayın harcamasını kullanıcının söylediği bütçeyle karşılaştırır.",
        "parameters": {"type": "object", "properties": {
            "amount": {"type": "number"}, "currencyCode": {"type": "string", "enum": ["TRY", "USD", "EUR", "GBP"]},
            "categoryCode": CATEGORY, "merchant": {"type": ["string", "null"], "default": None}},
            "required": ["amount", "currencyCode"]}}},
    {"type": "function", "function": {
        "name": "simulate_savings", "description": "Bir harcamayı azaltırsa ne kadar biriktireceğini hesaplar.",
        "parameters": {"type": "object", "properties": {
            "reductionPercent": {"type": "integer", "minimum": 1, "maximum": 100}, "categoryCode": CATEGORY,
            "merchant": {"type": ["string", "null"], "default": None},
            "months": {"type": "integer", "default": 6, "minimum": 1, "maximum": 12}}, "required": ["reductionPercent"]}}},
]
BUDGET_RESULT = {"period": {"kind": "ThisMonth", "startDate": "2026-10-01", "endDate": "2026-10-10"}, "currencyCode": "TRY",
                 "budgetAmount": 3000, "categoryCode": None, "categoryDisplayName": None, "merchantMatch": None,
                 "figures": {"spentAmount": Decimal("735.00"), "transactionCount": 5, "remainingAmount": Decimal("2265.00"),
                             "overAmount": 0, "isOverBudget": False, "elapsedDays": 10, "daysInMonth": 31,
                             "dailyAverage": Decimal("73.50"), "projectedMonthEnd": Decimal("2278.50"), "projectedOverAmount": 0,
                             "willExceed": False, "projectionMethod": "LinearDailyAverage"}}
SAVINGS_RESULT = {"from": "2026-04-01", "to": "2026-09-30", "months": 6, "reductionPercent": 20, "categoryCode": None,
                  "categoryDisplayName": None, "merchantMatch": {"status": "Matched", "merchant": "Starbucks", "isPartialMatch": False,
                                                                 "candidates": []},
                  "method": "FullMonthAverageTimesReduction", "currencies": []}
AMOUNT_QUESTION = "Bütçeniz ne kadar? Örneğin 3.000 TL."
CURRENCY_QUESTION = "Bütçeniz hangi para biriminde? 1) TL 2) Euro 3) Dolar 4) Sterlin"


def test_a_budget_in_the_users_words_runs():
    call = ToolCall("check_budget", {"amount": 3000, "currencyCode": "TRY"})
    chat, selector, gateway, explainer = service([call], gateway=FakeGateway(BUDGET_RESULT))

    reply = chat.answer("Bu ay 3.000 TL bütçem var, ne durumdayım?", TOKEN)

    assert (reply.type, gateway.calls[0][:2]) == ("answer", ("check_budget", {"amount": 3000, "currencyCode": "TRY"}))
    assert ("Kalan", "2.265,00 TL") in [(f.label, f.text) for f in explainer.calls[0][1]]


def test_a_misread_amount_is_asked_with_the_fixed_question_and_nothing_runs():
    writer = FakeWriter("Bütçeniz ne kadar, 1.000 TL mi?")
    chat, selector, gateway, explainer = service([ToolCall("check_budget", {"amount": 1.0, "currencyCode": "TRY"})], writer=writer)

    reply = chat.answer("Bu ay 1.000 TL bütçem var, ne durumdayım?", TOKEN)

    assert reply == ChatReply("clarification", AMOUNT_QUESTION, [], [])
    # A figure question is never worded by the model: it could suggest an amount (K3-A).
    assert writer.calls == [] and gateway.calls == []


def test_an_invented_amount_and_currency_are_asked_one_after_the_other():
    gateway = FakeGateway(BUDGET_RESULT)
    chat, selector, gateway, explainer = service([ToolCall("check_budget", {"amount": 5000, "currencyCode": "TRY"})], gateway=gateway)

    first = chat.answer("Bu ay bütçemi aşacak mıyım?", TOKEN)
    second = chat.answer("3000", TOKEN)
    third = chat.answer("1", TOKEN)

    assert (first.answer, second.answer, third.type) == (AMOUNT_QUESTION, CURRENCY_QUESTION, "answer")
    # The guessed 5000 and TRY never ran; the amount and the currency are the user's replies.
    assert gateway.calls == [("check_budget", {"amount": 3000, "currencyCode": "TRY"}, TOKEN, gateway.calls[0][3])]
    assert len(selector.calls) == 1


def test_an_amount_reply_that_names_the_currency_completes_both():
    gateway = FakeGateway(BUDGET_RESULT)
    chat, selector, gateway, explainer = service([ToolCall("check_budget", {"amount": 5000, "currencyCode": "TRY"})], gateway=gateway)

    chat.answer("Bu ay bütçemi aşacak mıyım?", TOKEN)
    reply = chat.answer("3.000 TL", TOKEN)

    assert reply.type == "answer"
    assert gateway.calls[0][:2] == ("check_budget", {"amount": 3000, "currencyCode": "TRY"})


def test_a_guessed_cut_is_asked_and_the_reply_completes_it():
    gateway = FakeGateway(SAVINGS_RESULT)
    call = ToolCall("simulate_savings", {"reductionPercent": 20, "merchant": "Starbucks"})
    chat, selector, gateway, explainer = service([call], gateway=gateway)

    asked = chat.answer("Starbucks harcamamı biraz azaltsam ne kazanırım?", TOKEN)
    chat.answer("%30", TOKEN)

    assert asked.answer == "Ne kadar azaltmayı düşünüyorsunuz? Örneğin %10, %25 ya da %50."
    assert gateway.calls[0][:2] == ("simulate_savings", {"merchant": "Starbucks", "reductionPercent": 30})


def test_bridge_errors_are_not_hidden():
    chat, *_ = service([SUMMARY_YESTERDAY], gateway=FakeGateway(error=SessionInvalid("expired")))
    with pytest.raises(SessionInvalid):
        chat.answer("Dün ne harcadım?", TOKEN)


def _real_ready() -> bool:
    try:
        return check_ready(OllamaSettings.from_env({"BANKINGAI_LLM_MODEL": "qwen3.5:4b", **os.environ})) == Readiness.READY
    except Exception:
        return False


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


@pytest.mark.skipif(not _real_ready(), reason="Ollama is not running or the model is not installed")
def test_real_model_picks_yesterday():
    from chat import LangChainToolSelector

    settings = OllamaSettings.from_env({"BANKINGAI_LLM_MODEL": "qwen3.5:4b", **os.environ})
    selector = LangChainToolSelector(settings)
    assert selector.select("Dün ne harcadım?", DEFINITIONS, selector.clock() + 30) == [SUMMARY_YESTERDAY]
