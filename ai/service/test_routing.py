import json
from pathlib import Path

import pytest

from ollama_client import OllamaTimeout
from routing import GENERAL, GROUPS, PLANNING, WEALTH, Route, Router, group_of, hide_dates, narrow

# CHAT-01: the tools a turn shows the model. Decisions K1-C (word rules first, the model only when none matches), K2-C (two
# groups: the nine tools of MCP-08 and the two planning tools; K2-B's three groups broke S15 and F01 on every run), K3-A (the
# general group when the model cannot tell), K5-B (clear out-of-scope requests answered without a model), K6-A (a follow-up's
# own rule first, else the last call's group). Expected values by hand.

CASES_FILE = Path(__file__).resolve().parent.parent / "model_check" / "tool_selection_cases.json"


class FakeClassifier:
    def __init__(self, answer="spending", error=None):
        self.answer, self.error, self.calls = answer, error, []

    def classify(self, message, deadline):
        self.calls.append((message, deadline))
        if self.error:
            raise self.error
        return self.answer


def route(message, previous=None, classifier=None):
    return Router(classifier or FakeClassifier(error=AssertionError("the model must not be asked"))).route(message, previous, 30.0)


@pytest.mark.parametrize(("message", "groups"), [
    ("Dün ne harcadım?", (GENERAL,)),
    ("Geçen ay en çok hangi kategoriye harcadım?", (GENERAL,)),
    ("Faturalara ne kadar harcadım?", (GENERAL,)),
    ("Son 3 ayda Starbucks'a ne kadar verdim?", (GENERAL,)),
    ("Geçen aya göre nasılım?", (GENERAL,)),
    ("Harcamam aylara göre nasıl değişti?", (GENERAL,)),
    ("Transferlerim harcamaya dahil mi?", (GENERAL,)),          # a question about transfers, not a request
    ("Aboneliklerim neler?", (GENERAL,)),                      # payment words keep the model's classifier out of it
    ("Önümüzdeki 7 günde hangi ödemelerim var?", (GENERAL,)),
    ("Netflix'e her ay ne ödüyorum?", (GENERAL,)),
    ("Bu ay 1.000 TL bütçem var, ne durumdayım?", (PLANNING,)),
    ("Bu ay 2.000 TL limit koydum, aşar mıyım?", (PLANNING,)),
    ("Starbucks için bu ay 300 lira ayırdım, ne kadar kaldı?", (PLANNING,)),  # eval S37: "ne kadar" is there too
    ("Starbucks harcamamı yarıya indirsem ne kazanırım?", (PLANNING,)),  # "harca" is there too: the specific group wins
    ("Market masrafımı %15 kıssam ne olur?", (PLANNING,)),
    ("Bütçemi aşmamak için aboneliklerimi azaltsam?", (PLANNING,)),      # a cut in the subscriptions: savings
])
def test_word_rules_choose_the_groups(message, groups):
    assert route(message) == Route(groups, source="rule")


@pytest.mark.parametrize("message", ["Deniz'e 100 TL transfer yap", "Anneme para gönder", "Hangi hisseye yatırım yapayım?",
                                     "Kripto alayım mı?", "Bana bir fıkra anlat", "Bir şaka yap"])
def test_clear_out_of_scope_requests_need_no_model(message):
    assert route(message) == Route((), out_of_scope=True, source="rule")


@pytest.mark.parametrize(("message", "previous", "groups"), [
    ("Peki geçen ay?", "check_budget", (PLANNING,)),
    ("Peki geçen ay?", "get_spending_summary", (GENERAL,)),
    ("Ya bu hafta?", "get_upcoming_payments", (GENERAL,)),
])
def test_a_follow_up_without_its_own_rule_keeps_the_last_calls_group(message, previous, groups):
    assert route(message, previous) == Route(groups, source="previous")


@pytest.mark.parametrize(("message", "previous", "groups"), [
    ("Aboneliklerim neler?", "check_budget", (GENERAL,)),
    ("Bir önceki ayla karşılaştır", "check_budget", (GENERAL,)),
    ("Peki 1.500 TL bütçe olsa?", "check_budget", (PLANNING,)),
])
def test_a_follow_ups_own_rule_comes_first(message, previous, groups):
    assert route(message, previous) == Route(groups, source="rule")


def test_the_model_is_asked_only_when_no_rule_matches():
    classifier = FakeClassifier(answer="payments")

    result = Router(classifier, clock=lambda: 0.0).route("Ayda sabit olarak neye para gidiyor?", None, 30.0)

    # The model still names three kinds (intent-v1); spending and payments share the general group.
    assert result == Route((GENERAL,), source="model")
    # At most 5 s for the question: a slow model falls back to the spending group instead of eating the turn.
    assert classifier.calls == [("Ayda sabit olarak neye para gidiyor?", 5.0)]


@pytest.mark.parametrize("classifier", [FakeClassifier(error=OllamaTimeout("slow")), FakeClassifier(answer="transfers"),
                                        FakeClassifier(answer=None)])
def test_without_a_usable_model_answer_the_general_group_is_used(classifier):
    assert Router(classifier).route("Ayda sabit olarak neye para gidiyor?", None, 30.0) == Route((GENERAL,), source="fallback")


def test_without_a_classifier_the_general_group_is_used():
    assert Router(None).route("Ayda sabit olarak neye para gidiyor?", None, 30.0) == Route((GENERAL,), source="fallback")


def test_the_model_can_send_a_question_to_planning():
    result = Router(FakeClassifier(answer="planning")).route("Ayda kenara ne koyabilirim?", None, 30.0)
    assert result == Route((PLANNING,), source="model")


def test_the_groups_cover_the_fifteen_published_tools_once_each():
    names = [name for tools in GROUPS.values() for name in tools]
    assert len(names) == len(set(names)) == 15
    assert len(GROUPS[GENERAL]) == 9 and group_of("check_budget") == PLANNING and group_of("unknown_tool") is None


def test_the_income_and_what_if_tools_stay_out_of_the_old_groups():
    # INC-03 PR1: the four tools are published on the server but in no route yet; without this row they would join every group.
    assert GROUPS[WEALTH] == ("get_income_share", "get_spending_advice", "what_if_one_off", "what_if_monthly")
    tools = [tool(name) for name in ("get_spending_summary", "check_budget", *GROUPS[WEALTH])]
    for group in (GENERAL, PLANNING):
        assert [t["function"]["name"] for t in narrow(tools, Route((group,), source="rule"))] == [
            "get_spending_summary" if group == GENERAL else "check_budget"]


def tool(name):
    return {"type": "function", "function": {"name": name, "parameters": {}}}


def test_narrowing_keeps_the_groups_tools_and_any_tool_the_table_does_not_know():
    tools = [tool("get_spending_summary"), tool("check_budget"), tool("simulate_savings"), tool("get_recurring_payments"),
             tool("get_spending_overview")]

    names = [t["function"]["name"] for t in narrow(tools, Route((PLANNING,), source="rule"))]

    # A tool added on the server but not to the table is never hidden: it would silently become unreachable.
    assert names == ["check_budget", "simulate_savings", "get_spending_overview"]


def test_every_eval_case_finds_its_tool_by_the_rules_alone():
    # The 63 tool-selection cases (MCP-17/18) before CHAT-01: the word rules must route every one of them to a group that holds
    # its expected tool, with no model at all; the "clarify" and out-of-scope ones may land anywhere but out of scope.
    cases = json.loads(CASES_FILE.read_text(encoding="utf-8"))["cases"]
    misses = []
    for case in cases:
        if case["id"].startswith("R"):
            continue
        previous = case["context"]["tool"] if case.get("context") else None
        result = Router(None).route(case["message"], previous, 30.0)
        expected = case["expected"]
        if expected in (None, "clarify"):
            continue
        tools = {e["tool"] for e in (expected if isinstance(expected, list) else [expected])}
        if result.out_of_scope or not tools <= result.tools():
            misses.append((case["id"], case["message"], result))
    assert misses == []


# --- CHAT-08: dates are never the model's to choose ----------------------------------------------------------------------

def _tool(name, properties):
    return {"type": "function", "function": {"name": name, "description": "Açıklama.",
                                             "parameters": {"type": "object", "properties": properties, "required": ["period"]}}}


def test_hide_dates_removes_the_range_and_the_custom_period_and_nothing_else():
    ranged = _tool("get_spending_summary", {"period": {"type": "string", "enum": ["Today", "ThisMonth", "Custom"]},
                                            "from": {"type": ["string", "null"]}, "to": {"type": ["string", "null"]}})
    plain = _tool("get_monthly_trend", {"months": {"type": "integer", "minimum": 1, "maximum": 12}})

    shown = hide_dates([ranged, plain])

    assert shown[0]["function"]["parameters"]["properties"] == {"period": {"type": "string", "enum": ["Today", "ThisMonth"]}}
    assert shown[0]["function"]["parameters"]["required"] == ["period"]
    assert shown[0]["function"]["description"] == "Açıklama."
    assert shown[1] == plain


def test_hide_dates_leaves_the_listed_tools_untouched():
    ranged = _tool("get_spending_summary", {"period": {"type": "string", "enum": ["Today", "Custom"]}, "from": {"type": "string"}})

    hide_dates([ranged])

    assert "from" in ranged["function"]["parameters"]["properties"]
    assert ranged["function"]["parameters"]["properties"]["period"]["enum"] == ["Today", "Custom"]


# --- INC-03 PR2: the wealth group by word rules (K3-A, K4-A, K5-A) -------------------------------------------------------

@pytest.mark.parametrize("message", [
    "Geçen ay maaşımın ne kadarını harcadım?",           # "harca" and "ne kadar" are there too: the wealth rule comes first
    "Gelirimin yüzde kaçını harcıyorum?",
    "Geçen ay nerede tasarruf edebilirim?",              # "tasarruf" is a planning word; this phrase is the advice question
    "Nereden tasarruf edebilirim?",
    "Tasarruf önerin var mı?",
    "Geçen ay eğlence harcamamı dolar alsaydım ne olurdu?",
    "Bu parayı harcamasaydım ne olurdu?",
    "Ayda 1.000 TL biriktirseydim ne olurdu?",           # "biriktir" is a planning word; -seydim makes it a what-if
    "Geçen yıl yatırım yapsaydım ne olurdu?",            # K5-A: in scope (it was out of scope before)
    "Ayda 500 TL altın olarak biriktirsem?",
    "Aylık 500 TL dolar cinsinden biriktirsem ne olur?",
])
def test_wealth_words_choose_the_wealth_group(message):
    # The group only; which what-if tool inside it the words choose is CHAT-09 K5-D's (below).
    chosen = route(message)
    assert (chosen.groups, chosen.source, chosen.out_of_scope) == ((WEALTH,), "rule", False)


@pytest.mark.parametrize("message", [
    "Bu ay 1.000 TL bütçem var, ne durumdayım?",
    "Market harcamamı %20 azaltsam ne kazanırım?",
    "Tasarruf etsem ne olur?",
    "Ayda 500 TL biriktirirsem ne kadar olur?",          # K4-A: "biriktiririm" is planning, only -seydim is a what-if
])
def test_old_planning_questions_stay_in_planning(message):
    assert route(message).groups == (PLANNING,)


@pytest.mark.parametrize("message", [
    "Altın alayım mı?", "Dolar yükselir mi?", "Euro düşer mi?", "Bana yatırım tavsiyesi verir misin?",
    "Hangisini almalıyım?", "Altın almak mantıklı mı?", "Ne alayım?",
])
def test_a_request_for_advice_or_a_forecast_is_answered_without_a_model(message):
    result = route(message)
    assert (result.out_of_scope, result.advice, result.source) == (True, True, "rule")


@pytest.mark.parametrize("message", ["Hangi hisseye yatırım yapayım?", "Kripto alayım mı?", "Borsada ne alayım?"])
def test_shares_and_crypto_stay_plainly_out_of_scope(message):
    assert route(message) == Route((), out_of_scope=True, source="rule")


def test_a_follow_up_of_a_wealth_call_stays_in_the_wealth_group():
    assert route("Peki geçen ay?", "get_income_share") == Route((WEALTH,), source="previous")


# --- CHAT-09 PR2 -------------------------------------------------------------------------------------------------------------

@pytest.mark.parametrize("message", ["Bu ay harcamalarım için 2.000 TL sınır belirledim, durum ne?", "Bu ay 1.000 TL sınır koydum, aşar mıyım?",
                                     "Sınırım 5 bin lira, ne durumdayım?", "BU AY SINIRIMI AŞAR MIYIM?"])
def test_a_spending_limit_is_a_planning_question(message):
    # K14-A: "sınır" in these shapes; tool-selection case R03 got the general group, which has no budget tool.
    assert route(message) == Route((PLANNING,), source="rule")


@pytest.mark.parametrize("message", ["Bu hafta çok sinir oldum, ne kadar harcadım?", "Sinirim bozuldu, dün ne harcadım?"])
def test_the_folded_word_for_nerves_is_no_limit(message):
    # "sinir" (nerve) and "sınır" (limit) fold to the same word: only the limit's own shapes count.
    assert route(message) == Route((GENERAL,), source="rule")


@pytest.mark.parametrize("message", ["Altın mı dolar mı alayım?", "Hangisini alayım?", "Bu ay dolar alayım", "Euro mu alayım altın mı?"])
def test_asking_what_to_buy_is_a_request_for_advice(message):
    # K15-A: in this app, "alayım" always asks what to buy.
    assert route(message) == Route((), out_of_scope=True, advice=True)


@pytest.mark.parametrize(("message", "only"), [
    ("Geçen ayki harcamalarımı biriktirseydim nolurdu", "what_if_one_off"),     # the simulator screenshot (9 Nov 2026)
    ("GECEN AYKI HARCAMALARIMI BIRIKTIRSEYDIM NOLURDU", "what_if_one_off"),
    ("Geçen ay harcadığım parayla altın alsaydım ne olurdu?", "what_if_one_off"),
    ("Geçen hafta harcamasaydım da euro alsaydım?", "what_if_one_off"),
    ("Geçen ay market masrafımı dolar alsaydım?", "what_if_one_off"),
    ("Ayda 1.000 TL altın olarak biriktirseydim ne olurdu?", "what_if_monthly"),
    ("Her ay 500 lira biriktirseydim?", "what_if_monthly"),
    ("Aylık 2 bin TL altın alsaydım ne olurdu?", "what_if_monthly"),
    ("AYDA BİRİKTİRSEYDİM NE OLURDU?", "what_if_monthly"),
])
def test_a_what_if_with_one_scenarios_words_shows_only_that_tool(message, only):
    # K5-D: the words name the scenario; the model is shown that one tool (the screenshot's question reached the monthly one).
    assert route(message) == Route((WEALTH,), source="rule", only=only)


@pytest.mark.parametrize("message", [
    "Biriktirseydim ne olurdu?",                                   # no scenario word: PR3 asks which one
    "Her ay harcadığım kadar biriktirseydim ne olurdu?",           # both
    "Geçen ay maaşımın ne kadarını harcadım?",                     # no what-if
    "Geçen ay nerede tasarruf edebilirim?",
])
def test_otherwise_the_whole_wealth_group_is_shown(message):
    assert route(message) == Route((WEALTH,), source="rule")


def test_narrowing_keeps_only_the_scenarios_tool():
    tools = [{"type": "function", "function": {"name": name}} for name in GROUPS[WEALTH]]
    assert [t["function"]["name"] for t in narrow(tools, Route((WEALTH,), only="what_if_one_off"))] == ["what_if_one_off"]
