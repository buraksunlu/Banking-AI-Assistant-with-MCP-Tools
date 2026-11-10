import json
import os
import re
from decimal import Decimal
from pathlib import Path

import pytest

from conversation import is_follow_up
from routing import GROUPS, Router, group_of
from suggestions import (OUT_OF_SCOPE_SUGGESTIONS, PATTERNS, PERIOD_NAMES, AskedStore, OfferedStore, Shape, StarterQuestions,
                         Suggestion, follow_ups, match, result_shape)
from test_mcp_gateway import _real_token, real
from mcp_gateway import McpSettings, McpToolGateway

TOKEN = "secret-token-123"
CASES = json.loads((Path(__file__).parent.parent / "model_check" / "tool_selection_cases.json").read_text("utf-8"))["cases"]

CATEGORIES, MERCHANTS, COMPARE = "Kategorilere göre?", "Peki işyerlerine göre?", "Bir önceki ayla karşılaştır"
HIGHEST, LAST_3_MONTHS = "En büyük harcamalarım neler?", "Ya son 3 ayda?"
UPCOMING_7 = "Önümüzdeki 7 günde hangi ödemelerim çıkacak?"
THIS_MONTH, RECURRING = "Bu ay ne kadar harcadım?", "Aboneliklerim neler?"
TOP_CATEGORY, LAST_MONTH_TOTAL = "Geçen ay en çok hangi kategoriye harcadım?", "Geçen ay toplam ne kadar harcadım?"
SAVE_WHERE, SAVE_ADVISED = "Peki nerede tasarruf edebilirim?", "Peki önerdiğin tutarı biriktirseydim?"
INCOME_LAST_MONTH = "Geçen ay maaşımın ne kadarını harcadım?"
AUGUST = {"period": "Custom", "from": "2026-08-01", "to": "2026-08-31"}


def s(text, tool, **arguments):
    return Suggestion(text, tool, arguments)


# Follow-ups (CHAT-10 PR2): the candidate table, written out by hand. K3-B: every text says its period and is a full question.

TM, LM = {"period": "ThisMonth"}, {"period": "LastMonth"}
CAT_TM, CAT_LM = "Bu ay kategorilere göre harcamam nasıl?", "Geçen ay kategorilere göre harcamam nasıl?"
MER_TM, MER_LM = "Bu ay harcamalarım işyerlerine göre nasıl dağıldı?", "Geçen ay harcamalarım işyerlerine göre nasıl dağıldı?"
HIGH_TM, HIGH_LM = "Bu ay en büyük harcamalarım neler?", "Geçen ay en büyük harcamalarım neler?"
TOTAL_TM, TOTAL_LM = "Bu ay toplam ne kadar harcadım?", "Geçen ay toplam ne kadar harcadım?"
SAME_DAYS = "Bu ay, geçen ayın aynı günlerine göre daha mı çok harcıyorum?"
ROSE = "Geçen aya göre harcamam arttı mı?"
TREND_6 = "Son 6 ayda harcamam aylara göre nasıl değişti?"
SAVE_WHERE_LM = "Geçen ay nerede tasarruf edebilirim?"
SAVE_ADVISED = "Önerdiğin tutarı her ay biriktirseydim ne olurdu?"
AUG = "1-31 Ağustos 2026 döneminde"


@pytest.mark.parametrize(
    ("tool", "arguments", "expected"),
    [
        ("get_spending_summary", TM,
         [s(CAT_TM, "get_category_breakdown", **TM), s(MER_TM, "get_merchant_breakdown", **TM),
          s(SAME_DAYS, "compare_spending", comparison="MonthToDate")]),
        ("get_spending_summary", LM,
         [s(CAT_LM, "get_category_breakdown", **LM), s(MER_LM, "get_merchant_breakdown", **LM),
          s(ROSE, "compare_spending", comparison="PreviousFullMonth")]),
        # No comparison for a day or a week: the biggest spending instead.
        ("get_spending_summary", {"period": "Yesterday"},
         [s("Dün kategorilere göre harcamam nasıl?", "get_category_breakdown", period="Yesterday"),
          s("Dün harcamalarım işyerlerine göre nasıl dağıldı?", "get_merchant_breakdown", period="Yesterday"),
          s("Dün en büyük harcamalarım neler?", "get_highest_transactions", period="Yesterday")]),
        ("get_category_breakdown", TM,
         [s(MER_TM, "get_merchant_breakdown", **TM), s(HIGH_TM, "get_highest_transactions", **TM),
          s(CAT_LM, "get_category_breakdown", **LM)]),
        # A period with no counterpart turns to this month.
        ("get_category_breakdown", {"period": "Last3Months"},
         [s("Son 3 ayda harcamalarım işyerlerine göre nasıl dağıldı?", "get_merchant_breakdown", period="Last3Months"),
          s("Son 3 ayda en büyük harcamalarım neler?", "get_highest_transactions", period="Last3Months"),
          s(CAT_TM, "get_category_breakdown", **TM)]),
        # P3-A: the merchant's name takes no ending.
        ("get_merchant_spending", {"merchant": "Starbucks", "period": "LastMonth"},
         [s("Son 3 ayda Starbucks harcamam ne kadar?", "get_merchant_spending", merchant="Starbucks", period="Last3Months"),
          s(MER_LM, "get_merchant_breakdown", **LM),
          s("Bu ay Starbucks harcamam ne kadar?", "get_merchant_spending", merchant="Starbucks", period="ThisMonth")]),
        ("get_merchant_breakdown", {"period": "ThisWeek"},
         [s("Bu hafta kategorilere göre harcamam nasıl?", "get_category_breakdown", period="ThisWeek"),
          s("Bu hafta en büyük harcamalarım neler?", "get_highest_transactions", period="ThisWeek"),
          s("Geçen hafta harcamalarım işyerlerine göre nasıl dağıldı?", "get_merchant_breakdown", period="LastWeek")]),
        ("get_highest_transactions", {"period": "ThisYear"},
         [s("Bu yıl kategorilere göre harcamam nasıl?", "get_category_breakdown", period="ThisYear"),
          s("Bu yıl harcamalarım işyerlerine göre nasıl dağıldı?", "get_merchant_breakdown", period="ThisYear"),
          s(HIGH_TM, "get_highest_transactions", **TM)]),
        ("compare_spending", {"comparison": "MonthToDate"},
         [s(CAT_TM, "get_category_breakdown", **TM), s(TREND_6, "get_monthly_trend", months=6),
          s(HIGH_TM, "get_highest_transactions", **TM)]),
        ("compare_spending", {"comparison": "PreviousFullMonth"},
         [s(CAT_LM, "get_category_breakdown", **LM), s(TREND_6, "get_monthly_trend", months=6),
          s(HIGH_LM, "get_highest_transactions", **LM)]),
        ("get_monthly_trend", {"months": 3},
         [s(ROSE, "compare_spending", comparison="PreviousFullMonth"), s(CAT_LM, "get_category_breakdown", **LM),
          s(TOTAL_TM, "get_spending_summary", **TM)]),
        ("get_recurring_payments", {}, [s(UPCOMING_7, "get_upcoming_payments", days=7), s(TREND_6, "get_monthly_trend", months=6)]),
        ("get_upcoming_payments", {"days": 7}, [s(RECURRING, "get_recurring_payments"), s(TOTAL_TM, "get_spending_summary", **TM)]),
        ("check_budget", {"amount": Decimal("1000"), "currencyCode": "TRY"},
         [s(MER_TM, "get_merchant_breakdown", **TM), s(CAT_TM, "get_category_breakdown", **TM),
          s(HIGH_TM, "get_highest_transactions", **TM)]),
        ("simulate_savings", {"reductionPercent": 50, "merchant": "Starbucks"},
         [s(TREND_6, "get_monthly_trend", months=6), s(CAT_LM, "get_category_breakdown", **LM)]),
        # INC-03 K19-B: income share → advice → the advised saving; the what-ifs offer nothing.
        ("get_income_share", LM, [s(SAVE_WHERE_LM, "get_spending_advice", **LM), s(CAT_LM, "get_category_breakdown", **LM)]),
        # P2-A: a range is written out by code, so the text runs (and reads, when typed) the same days.
        ("get_income_share", AUGUST,
         [s(f"{AUG} nerede tasarruf edebilirim?", "get_spending_advice", **AUGUST),
          s(f"{AUG} kategorilere göre harcamam nasıl?", "get_category_breakdown", **AUGUST)]),
        ("get_spending_advice", LM,
         [s(SAVE_ADVISED, "what_if_monthly", fromAdvice=True), s(INCOME_LAST_MONTH, "get_income_share", **LM),
          s(CAT_LM, "get_category_breakdown", **LM)]),
        # The advised saving is always last month's: offered under another month's advice it would answer a different month.
        ("get_spending_advice", AUGUST, []),
        ("what_if_one_off", LM, []),
        ("what_if_monthly", {"fromAdvice": True}, []),
    ],
)
def test_each_tool_has_its_follow_ups(tool, arguments, expected):
    assert follow_ups(tool, arguments) == expected


def test_a_range_carries_its_days_into_every_follow_up_of_the_same_period():
    offered = follow_ups("get_spending_summary", AUGUST)
    assert [o.text for o in offered] == [f"{AUG} kategorilere göre harcamam nasıl?",
                                         f"{AUG} harcamalarım işyerlerine göre nasıl dağıldı?", f"{AUG} en büyük harcamalarım neler?"]
    assert all(o.arguments == {"period": "Custom", "from": "2026-08-01", "to": "2026-08-31"} for o in offered)


def test_the_call_that_just_ran_is_not_offered_again():
    # "Son 3 ayda Starbucks" after a three-month question would run the very same call.
    assert follow_ups("get_merchant_spending", {"merchant": "Starbucks", "period": "Last3Months"}) == [
        s("Son 3 ayda harcamalarım işyerlerine göre nasıl dağıldı?", "get_merchant_breakdown", period="Last3Months"),
        s("Bu ay Starbucks harcamam ne kadar?", "get_merchant_spending", merchant="Starbucks", period="ThisMonth")]


# K1-C: nothing asked in the conversation is offered again; P4-A: with no candidate left, two general questions not asked yet.

SUMMARY_TM, CATEGORIES_TM = ("get_spending_summary", TM), ("get_category_breakdown", TM)
MERCHANTS_TM, HIGHEST_TM = ("get_merchant_breakdown", TM), ("get_highest_transactions", TM)


def test_the_ping_pong_of_breakdowns_is_gone():
    # The INC-04 trial: summary → categories → merchants offered "Kategorilere göre?" again, and the biggest ones offered both.
    assert [o.text for o in follow_ups(*MERCHANTS_TM, asked=[SUMMARY_TM, CATEGORIES_TM])] == [HIGH_TM, MER_LM]
    assert [o.text for o in follow_ups(*HIGHEST_TM, asked=[SUMMARY_TM, CATEGORIES_TM, MERCHANTS_TM])] == [HIGH_LM]


def test_the_ping_pong_of_payments_is_gone():
    assert [o.text for o in follow_ups("get_upcoming_payments", {"days": 7}, asked=[("get_recurring_payments", {})])] == [TOTAL_TM]


def test_the_income_share_is_not_offered_right_after_it_was_asked():
    asked = [("get_income_share", LM)]
    assert [o.text for o in follow_ups("get_spending_advice", LM, asked=asked)] == [SAVE_ADVISED, CAT_LM]


def test_with_every_candidate_asked_two_general_questions_not_asked_yet_are_offered():
    asked = [CATEGORIES_TM, MERCHANTS_TM, ("get_highest_transactions", LM), SUMMARY_TM]
    assert follow_ups(*HIGHEST_TM, asked=asked) == [s(ROSE, "compare_spending", comparison="PreviousFullMonth"),
                                                    s(RECURRING, "get_recurring_payments")]


def test_an_answer_that_offers_nothing_on_purpose_is_not_filled():
    assert follow_ups("what_if_one_off", LM, asked=[SUMMARY_TM]) == []
    assert follow_ups("get_spending_advice", AUGUST, asked=[SUMMARY_TM]) == []


# K2-B: the answer is looked at by counts only, never by a figure.

def test_no_spending_offers_only_another_period():
    assert follow_ups(*SUMMARY_TM, shape=Shape(spent=False)) == [s(TOTAL_LM, "get_spending_summary", **LM)]
    assert follow_ups("get_merchant_spending", {"merchant": "Zara", "period": "LastMonth"}, shape=Shape(spent=False)) == [
        s("Son 3 ayda Zara harcamam ne kadar?", "get_merchant_spending", merchant="Zara", period="Last3Months"),
        s("Bu ay Zara harcamam ne kadar?", "get_merchant_spending", merchant="Zara", period="ThisMonth")]


def test_one_purchase_is_not_broken_down():
    assert follow_ups(*SUMMARY_TM, shape=Shape(spent=True, transactions=1)) == [
        s(SAME_DAYS, "compare_spending", comparison="MonthToDate"), s(TOTAL_LM, "get_spending_summary", **LM)]


def test_no_regular_payment_offers_no_upcoming_payment():
    assert follow_ups("get_recurring_payments", {}, shape=Shape(payments=0)) == [s(TREND_6, "get_monthly_trend", months=6)]


@pytest.mark.parametrize(("tool", "result", "shape"), [
    ("get_spending_summary", {"currencies": []}, Shape(spent=False, transactions=0)),
    ("get_spending_summary", {"currencies": [{"totalAmount": Decimal("85.00"), "transactionCount": 1},
                                             {"totalAmount": Decimal("10.00"), "transactionCount": 2}]}, Shape(spent=True, transactions=3)),
    ("get_category_breakdown", {"currencies": []}, Shape(spent=False)),
    ("get_category_breakdown", {"currencies": [{"categories": [{}, {}]}]}, Shape(spent=True)),
    ("get_merchant_breakdown", {"currencies": [{"merchants": [{}]}]}, Shape(spent=True)),
    ("get_highest_transactions", {"currencies": [{"items": [{}]}]}, Shape(spent=True)),
    ("get_highest_transactions", {"currencies": [{"items": []}]}, Shape(spent=False)),
    ("get_merchant_spending", {"currencies": []}, Shape(spent=False)),
    ("get_recurring_payments", {"payments": []}, Shape(payments=0)),
    ("get_upcoming_payments", {"payments": [{}, {}]}, Shape(payments=2)),
    ("compare_spending", {"currencies": []}, Shape()),
    ("get_category_breakdown", {"unexpected": True}, Shape()),     # a result the code cannot read changes nothing
])
def test_the_shape_of_a_result_is_its_counts(tool, result, shape):
    assert result_shape(tool, result) == shape


def test_a_tool_the_table_does_not_know_gets_none():
    assert follow_ups("get_accounts", {}) == []


def test_no_tool_gets_more_than_three():
    for group in GROUPS.values():
        for tool in group:
            for period in ("ThisMonth", "LastMonth", "Yesterday"):
                assert len(follow_ups(tool, {"period": period, "comparison": "MonthToDate", "months": 6, "merchant": "Migros"})) <= 3


def test_an_offered_list_is_a_copy_the_caller_cannot_change():
    first = follow_ups("get_spending_summary", {"period": "ThisMonth"})
    first[0].arguments["period"] = "Today"
    assert follow_ups("get_spending_summary", {"period": "ThisMonth"})[0].arguments == {"period": "ThisMonth"}


# The texts must also work when they are typed, or the ten minutes are over: then they take the normal path.
def every_follow_up():
    arguments = {"period": "ThisMonth", "comparison": "MonthToDate", "months": 3, "merchant": "Starbucks", "days": 7,
                 "amount": Decimal("1000"), "currencyCode": "TRY", "reductionPercent": 50}
    return [(tool, suggestion) for group in GROUPS.values() for tool in group for suggestion in follow_ups(tool, arguments)]


@pytest.mark.parametrize(("previous", "suggestion"), every_follow_up())
def test_a_typed_follow_up_is_routed_to_its_tools_group(previous, suggestion):
    previous_tool = previous if is_follow_up(suggestion.text) else None
    route = Router().route(suggestion.text, previous_tool, deadline=30.0)
    assert route.groups == (group_of(suggestion.tool),), suggestion.text


# P1-B: a text with a period is a pattern; each one is a measured case for two periods (one where only one is offered) and its
# phrasing template covers every period it is offered with, so the modelless phrasing test runs them all.

PHRASING = json.loads((Path(__file__).parent.parent / "model_check" / "phrasing_templates.json").read_text("utf-8"))


def _cases_of(pattern):
    shape = re.escape(pattern).replace(re.escape("{period}"), "(?P<period>.+?)").replace(re.escape("{merchant}"), r"\S+")
    return [case for case in CASES if "context" not in case and isinstance(case["expected"], (dict, list))
            and re.fullmatch(shape, case["message"]) and re.fullmatch(shape, case["message"]).group("period") in PERIOD_NAMES.values()]


@pytest.mark.parametrize("pattern", PATTERNS)
def test_every_pattern_offered_is_a_measured_case(pattern):
    named = [p for p in PATTERNS[pattern] if p != "Custom"]
    assert len({c["message"] for c in _cases_of(pattern)}) >= min(2, len(named)), pattern


@pytest.mark.parametrize("pattern", PATTERNS)
def test_every_pattern_is_a_phrasing_template_for_each_of_its_periods(pattern):
    offered = next(intent for intent in PHRASING["intents"] if intent["id"] == "offered")
    template = next((t for t in offered["templates"] if t["text"] == pattern), None)
    assert template is not None, pattern
    slots = template.get("slots", offered.get("slots", {}))
    assert {slot["args"]["period"] for slot in slots["period"]} == set(PATTERNS[pattern]), pattern


@pytest.mark.parametrize("text", [INCOME_LAST_MONTH, TOTAL_TM, TOTAL_LM, RECURRING, UPCOMING_7, ROSE, TREND_6, SAME_DAYS,
                                  SAVE_ADVISED, SAVE_WHERE_LM])
def test_every_full_question_offered_is_a_measured_case(text):
    # A full question is not read with the last call: it goes to the model like any other, so only measured ones are offered.
    assert any(case["message"] == text and "context" not in case and isinstance(case["expected"], (dict, list)) for case in CASES)


# Matching a reply against the offered suggestions (K3-C).

OFFERED = [s(CATEGORIES, "get_category_breakdown", period="ThisMonth"), s(MERCHANTS, "get_merchant_breakdown", period="ThisMonth")]


@pytest.mark.parametrize("message", ["Kategorilere göre?", "kategorilere göre", "  KATEGORİLERE   göre ?  ", "Kategorilere göre."])
def test_the_offered_text_matches_whatever_its_case_spaces_and_end(message):
    assert match(OFFERED, message) == OFFERED[0]


@pytest.mark.parametrize("message", ["Kategorilere göre ne?", "Kategoriler", "Peki geçen ay?", ""])
def test_another_text_matches_nothing(message):
    assert match(OFFERED, message) is None


def test_nothing_offered_matches_nothing():
    assert match([], "Kategorilere göre?") is None


class FakeClock:
    def __init__(self):
        self.now = 0.0

    def __call__(self):
        return self.now


def test_the_offer_is_taken_once_and_only_by_its_token():
    store = OfferedStore()
    store.put(TOKEN, OFFERED)

    assert store.take("another-users-token") == []
    assert store.take(TOKEN) == OFFERED
    assert store.take(TOKEN) == []


def test_the_offer_is_forgotten_after_ten_minutes():
    clock = FakeClock()
    store = OfferedStore(clock=clock)
    store.put(TOKEN, OFFERED)

    clock.now = 600.5
    assert store.take(TOKEN) == []


# Starters (K4-B): three yes/no checks, then the first three questions whose condition holds.


class FakeGateway:
    def __init__(self, upcoming, this_month, recurring):
        self.results = {"get_upcoming_payments": {"payments": upcoming}, "get_spending_summary": {"currencies": this_month},
                        "get_recurring_payments": {"payments": recurring}}
        self.calls = []

    def call(self, tool, arguments, token, deadline):
        self.calls.append((tool, arguments, token, deadline))
        return self.results[tool]


NETFLIX = {"merchant": "Netflix"}
OCTOBER = {"currencyCode": "TRY", "totalAmount": Decimal("735.00")}


@pytest.mark.parametrize(
    ("upcoming", "this_month", "recurring", "expected"),
    [
        # Ada on 10 Oct 2026: Netflix in two days, 735,00 TL in October, Netflix and Spotify every month.
        ([NETFLIX], [OCTOBER], [NETFLIX, {"merchant": "Spotify"}], [UPCOMING_7, THIS_MONTH, RECURRING]),
        # Deniz and Ege: spending this month, no recurring payment.
        ([], [OCTOBER], [], [THIS_MONTH, TOP_CATEGORY, LAST_MONTH_TOTAL]),
        # A recurring payment that is not due within seven days.
        ([], [OCTOBER], [NETFLIX], [THIS_MONTH, RECURRING, TOP_CATEGORY]),
        # Nothing this month and nothing recurring: two questions, nothing made up to fill the third.
        ([], [], [], [TOP_CATEGORY, LAST_MONTH_TOTAL]),
    ],
)
def test_starters_follow_the_customers_data(upcoming, this_month, recurring, expected):
    gateway = FakeGateway(upcoming, this_month, recurring)
    starters = StarterQuestions(gateway, clock=FakeClock())

    assert starters.for_token(TOKEN) == expected


def test_starters_ask_three_tools_with_the_users_token_and_a_short_limit():
    gateway = FakeGateway([], [], [])
    StarterQuestions(gateway, clock=FakeClock()).for_token(TOKEN)

    assert [(tool, arguments, token) for tool, arguments, token, _ in gateway.calls] == [
        ("get_upcoming_payments", {"days": 7}, TOKEN), ("get_spending_summary", {"period": "ThisMonth"}, TOKEN),
        ("get_recurring_payments", {}, TOKEN)]
    assert {deadline for *_, deadline in gateway.calls} == {5.0}


def test_out_of_scope_suggestions_are_always_answerable_questions():
    assert OUT_OF_SCOPE_SUGGESTIONS == (THIS_MONTH, TOP_CATEGORY, LAST_MONTH_TOTAL)


@real
def test_real_server_gives_ada_her_three_starters():
    starters = StarterQuestions(McpToolGateway(McpSettings.from_env(os.environ)))
    assert starters.for_token(_real_token()) == [UPCOMING_7, THIS_MONTH, RECURRING]


# CHAT-10 K10-A: a failed follow-up gives its offer back, but never over a newer offer, and an empty offer is nothing to keep.
def test_an_offer_is_given_back_only_when_nothing_newer_was_made():
    store = OfferedStore()
    old, new = [Suggestion("Eski?", "get_spending_summary")], [Suggestion("Yeni?", "get_category_breakdown")]
    store.put_back("t", old)
    assert store.take("t") == old
    store.put("t", new)
    store.put_back("t", old)
    assert store.take("t") == new
    store.put_back("t", [])
    assert store.take("t") == []


# K1-C: what was asked in the conversation, kept per token for thirty minutes after the last call.

def test_the_calls_asked_are_kept_per_token_for_thirty_minutes():
    clock = FakeClock()
    store = AskedStore(clock=clock)
    store.remember(TOKEN, "get_spending_summary", {"period": "ThisMonth"})
    clock.now = 1000.0
    store.remember(TOKEN, "get_category_breakdown", {"period": "ThisMonth"})
    assert store.recall("another-users-token") == []
    assert store.recall(TOKEN) == [("get_spending_summary", {"period": "ThisMonth"}), ("get_category_breakdown", {"period": "ThisMonth"})]
    clock.now = 1000.0 + 1800.5
    assert store.recall(TOKEN) == []


def test_the_same_call_asked_twice_is_kept_once_and_the_list_is_bounded():
    store = AskedStore()
    for _ in range(2):
        store.remember(TOKEN, "get_spending_summary", {"period": "ThisMonth"})
    assert store.recall(TOKEN) == [("get_spending_summary", {"period": "ThisMonth"})]
    for months in range(1, 60):
        store.remember(TOKEN, "get_monthly_trend", {"months": months})
    kept = store.recall(TOKEN)
    assert len(kept) == 50 and kept[-1] == ("get_monthly_trend", {"months": 59})
    assert kept.count(("get_spending_summary", {"period": "ThisMonth"})) <= 1
