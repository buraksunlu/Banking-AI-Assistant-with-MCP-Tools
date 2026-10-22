import json
import os
from decimal import Decimal
from pathlib import Path

import pytest

from conversation import is_follow_up
from routing import GROUPS, Router, group_of
from suggestions import (OUT_OF_SCOPE_SUGGESTIONS, OfferedStore, StarterQuestions, Suggestion, follow_ups, match)
from test_mcp_gateway import _real_token, real
from mcp_gateway import McpSettings, McpToolGateway

TOKEN = "secret-token-123"
CASES = json.loads((Path(__file__).parent.parent / "model_check" / "tool_selection_cases.json").read_text("utf-8"))["cases"]

CATEGORIES, MERCHANTS, COMPARE = "Kategorilere göre?", "Peki işyerlerine göre?", "Bir önceki ayla karşılaştır"
HIGHEST, LAST_3_MONTHS = "En büyük harcamalarım neler?", "Ya son 3 ayda?"
UPCOMING_7 = "Önümüzdeki 7 günde hangi ödemelerim çıkacak?"
THIS_MONTH, RECURRING = "Bu ay ne kadar harcadım?", "Aboneliklerim neler?"
TOP_CATEGORY, LAST_MONTH_TOTAL = "Geçen ay en çok hangi kategoriye harcadım?", "Geçen ay toplam ne kadar harcadım?"


def s(text, tool, **arguments):
    return Suggestion(text, tool, arguments)


# Follow-ups (K5-A): the table, written out by hand.


@pytest.mark.parametrize(
    ("tool", "arguments", "expected"),
    [
        ("get_spending_summary", {"period": "ThisMonth"},
         [s(CATEGORIES, "get_category_breakdown", period="ThisMonth"), s(MERCHANTS, "get_merchant_breakdown", period="ThisMonth"),
          s(COMPARE, "compare_spending", comparison="MonthToDate")]),
        ("get_spending_summary", {"period": "LastMonth"},
         [s(CATEGORIES, "get_category_breakdown", period="LastMonth"), s(MERCHANTS, "get_merchant_breakdown", period="LastMonth"),
          s(COMPARE, "compare_spending", comparison="PreviousFullMonth")]),
        # No comparison for a day or a week: the biggest spending instead.
        ("get_spending_summary", {"period": "Yesterday"},
         [s(CATEGORIES, "get_category_breakdown", period="Yesterday"), s(MERCHANTS, "get_merchant_breakdown", period="Yesterday"),
          s(HIGHEST, "get_highest_transactions", period="Yesterday")]),
        ("get_category_breakdown", {"period": "ThisMonth"},
         [s(MERCHANTS, "get_merchant_breakdown", period="ThisMonth"), s(HIGHEST, "get_highest_transactions", period="ThisMonth"),
          s("Peki geçen ay?", "get_category_breakdown", period="LastMonth")]),
        ("get_category_breakdown", {"period": "Last3Months"},
         [s(MERCHANTS, "get_merchant_breakdown", period="Last3Months"), s(HIGHEST, "get_highest_transactions", period="Last3Months")]),
        ("get_merchant_spending", {"merchant": "Starbucks", "period": "LastMonth"},
         [s(LAST_3_MONTHS, "get_merchant_spending", merchant="Starbucks", period="Last3Months"),
          s(MERCHANTS, "get_merchant_breakdown", period="LastMonth")]),
        ("get_merchant_breakdown", {"period": "ThisWeek"},
         [s(CATEGORIES, "get_category_breakdown", period="ThisWeek"), s(HIGHEST, "get_highest_transactions", period="ThisWeek")]),
        ("get_highest_transactions", {"period": "ThisYear"},
         [s(CATEGORIES, "get_category_breakdown", period="ThisYear"), s(MERCHANTS, "get_merchant_breakdown", period="ThisYear")]),
        ("compare_spending", {"comparison": "MonthToDate"},
         [s(CATEGORIES, "get_category_breakdown", period="ThisMonth"), s("6 aylık göster", "get_monthly_trend", months=6)]),
        ("compare_spending", {"comparison": "PreviousFullMonth"},
         [s(CATEGORIES, "get_category_breakdown", period="LastMonth"), s("6 aylık göster", "get_monthly_trend", months=6)]),
        ("get_monthly_trend", {"months": 3},
         [s("Geçen aya göre harcamam arttı mı?", "compare_spending", comparison="PreviousFullMonth"),
          s(TOP_CATEGORY, "get_category_breakdown", period="LastMonth")]),
        ("get_recurring_payments", {}, [s(UPCOMING_7, "get_upcoming_payments", days=7)]),
        ("get_upcoming_payments", {"days": 7}, [s(RECURRING, "get_recurring_payments")]),
        ("check_budget", {"amount": Decimal("1000"), "currencyCode": "TRY"},
         [s("Bu ay en çok hangi işyerine harcadım?", "get_merchant_breakdown", period="ThisMonth"),
          s(CATEGORIES, "get_category_breakdown", period="ThisMonth")]),
        ("simulate_savings", {"reductionPercent": 50, "merchant": "Starbucks"},
         [s("Son 6 ayda harcamam aylara göre nasıl değişti?", "get_monthly_trend", months=6),
          s(TOP_CATEGORY, "get_category_breakdown", period="LastMonth")]),
    ],
)
def test_each_tool_has_its_follow_ups(tool, arguments, expected):
    assert follow_ups(tool, arguments) == expected


def test_the_call_that_just_ran_is_not_offered_again():
    # "Ya son 3 ayda?" after a three-month question would run the very same call.
    assert follow_ups("get_merchant_spending", {"merchant": "Starbucks", "period": "Last3Months"}) == [
        s(MERCHANTS, "get_merchant_breakdown", period="Last3Months")]


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


@pytest.mark.parametrize("text", [THIS_MONTH, TOP_CATEGORY, LAST_MONTH_TOTAL, RECURRING, UPCOMING_7,
                                  "Bu ay en çok hangi işyerine harcadım?", "Geçen aya göre harcamam arttı mı?",
                                  "Son 6 ayda harcamam aylara göre nasıl değişti?"])
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
