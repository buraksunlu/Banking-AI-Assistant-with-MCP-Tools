"""Simulator trial (19 Nov 2026): "Bu ay geçen aya göre nasıl gidiyorum?" got the fixed "Sorunuzu tam anlayamadım" on the phone
and, on another run, last month against the month before. The comparison words choose the tool, and code chooses which comparison:
"bu ay" is this month so far against the same days of last month, otherwise last month against the month before (the tool's own
description, cases S17/S18/R06)."""

from datetime import date

import pytest

from chat import ToolCall
from dates import CustomerDay
from routing import GENERAL, Router, comparison_of, fold_turkish
from test_cards import COMPARISON
from test_chat import DEFINITIONS, TOKEN, FakeGateway, service

DAY = CustomerDay(date(2026, 11, 19), "Europe/Istanbul")
COMPARE = {"type": "function", "function": {
    "name": "compare_spending", "description": "Harcamayı önceki dönemle karşılaştırır.",
    "parameters": {"type": "object", "properties": {
        "comparison": {"type": "string", "enum": ["PreviousFullMonth", "MonthToDate"]}}, "required": ["comparison"]}}}


class CompareGateway(FakeGateway):
    def list_tools(self, token, deadline):
        super().list_tools(token, deadline)
        return DEFINITIONS + [COMPARE]


@pytest.mark.parametrize("message, comparison", [
    ("Bu ay geçen aya göre nasıl gidiyorum?", "MonthToDate"),
    ("bu ay gecen aya gore daha mi cok harciyorum", "MonthToDate"),
    ("Bu ay, geçen ayın aynı günlerine göre daha mı çok harcıyorum?", "MonthToDate"),
    ("Geçen aya göre harcamam arttı mı?", "PreviousFullMonth"),
    ("Geçen aya göre daha mı çok harcadım?", "PreviousFullMonth"),
    ("Önceki aya kıyasla nasılım?", "PreviousFullMonth"),
])
def test_the_words_choose_the_comparison(message, comparison):
    assert comparison_of(fold_turkish(message)) == comparison
    assert Router().route(message, None, 1e18).only == "compare_spending"


@pytest.mark.parametrize("message", ["Geçen ay ne kadar harcadım?", "Bu ay en çok nereye harcadım?", "Ayın 5'inde ne aldım?"])
def test_other_questions_are_no_comparison(message):
    assert comparison_of(fold_turkish(message)) is None
    assert Router().route(message, None, 1e18).only != "compare_spending"


def test_code_overrides_the_model_s_comparison():
    gateway = CompareGateway(COMPARISON)
    chat, selector, _, _ = service([ToolCall("compare_spending", {"comparison": "PreviousFullMonth"})], gateway=gateway)

    chat.answer("Bu ay geçen aya göre nasıl gidiyorum?", TOKEN, DAY)

    # The model is shown the comparison alone and its guess is put right by the words.
    assert [t["function"]["name"] for t in selector.calls[0][1]] == ["compare_spending"]
    assert gateway.calls[0][:2] == ("compare_spending", {"comparison": "MonthToDate"})


def test_a_model_that_chooses_nothing_still_compares():
    gateway = CompareGateway(COMPARISON)
    chat, _, _, _ = service([], gateway=gateway)

    chat.answer("Geçen aya göre harcamam arttı mı?", TOKEN, DAY)

    assert gateway.calls[0][:2] == ("compare_spending", {"comparison": "PreviousFullMonth"})


# Moved from test_routing (19 Nov 2026): still the general group, now with the comparison alone; a follow-up's own rule first.
@pytest.mark.parametrize("message, previous", [("Geçen aya göre nasılım?", None), ("Harcamam geçen aya göre nasıl değişti?", None),
                                               ("Bir önceki ayla karşılaştır", "check_budget")])
def test_the_route_keeps_the_general_group(message, previous):
    route = Router().route(message, previous, 1e18)
    assert (route.groups, route.source, route.only) == ((GENERAL,), "rule", "compare_spending")
