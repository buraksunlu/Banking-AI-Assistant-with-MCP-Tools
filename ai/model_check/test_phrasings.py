"""CHAT-09 PR1: the phrasing generator and the model-free run of every phrasing through the chat's own code.

Decisions: K1-B (model-free wide, model-run sample), K2-C (templates plus real phrasings), K3-C (typos are reported, never gating),
K4-B (every model-free phrasing passes or is a known gap with a written reason). Expected values by hand.
"""

import json

import pytest

from chat import ToolCall
from conversation import LastCall
from phrasings import (DAY, GAPS_FILE, TOOLS_FILE, Captured, OracleSelector, Outcome, ProbeGateway, Variant, expand,
                       load_tools, matches, outcome_of, probe, run_variant, sample, turkish_lower, turkish_upper, transform)
from prompts import ADVICE_DECLINED_TEXT, CLARIFY_TEXT, LIRA_ONLY_TEXT, OUT_OF_SCOPE_TEXT
from chat import ChatReply, DATE_UNSUPPORTED_TEXT

PERIODS = [{"text": "geçen ay", "args": {"period": "LastMonth"}}, {"text": "dün", "args": {"period": "Yesterday"}}]
DATA = {"intents": [
    {"id": "summary", "slots": {"period": PERIODS}, "templates": [
        {"id": "t1", "text": "{period} ne kadar harcadım?", "expected": {"tool": "get_spending_summary", "arguments": {}}},
        {"id": "t2", "text": "Ne kadar harcadım?", "expected": "clarify",
         "oracle": {"tool": "get_spending_summary", "arguments": {"period": "ThisMonth"}}}]},
    {"id": "savings", "templates": [
        {"id": "t1", "text": "Starbucks harcamamı yarıya indirsem ne olur?", "args": {"reductionPercent": 50},
         "expected": [{"tool": "simulate_savings", "arguments": {"merchant": "Starbucks"}},
                      {"tool": "simulate_savings", "arguments": {"merchant": "Starbucks", "months": 6}}]}]},
], "real": [{"id": "r01", "text": "Geçen ayki harcamalarımı biriktirseydim nolurdu", "expected": "clarify"}],
    "dialogs": [{"id": "d01", "turns": ["Ayda biriktirseydim ne olurdu?", "5000"], "oracle": {"tool": "what_if_monthly", "arguments": {}},
                 "expected": {"tool": "what_if_monthly", "arguments": {"amount": 5000, "currencyCode": "TRY"}}}]}


# --- Transformations --------------------------------------------------------------------------------------------------------

@pytest.mark.parametrize(("name", "text", "result"), [
    ("ascii", "Geçen ayki harcamalarımı biriktirseydim?", "Gecen ayki harcamalarimi biriktirseydim?"),
    ("ascii", "İŞYERİ ÖĞÜN", "ISYERI OGUN"),
    ("upper", "geçen ay ne kadar harcadım?", "GEÇEN AY NE KADAR HARCADIM?"),
    ("upper", "bu yıl işyerine", "BU YIL İŞYERİNE"),                    # a Turkish keyboard: i → İ, ı → I
    ("lower", "Geçen Ay İşyerine ne kadar verdim?", "geçen ay işyerine ne kadar verdim"),
    ("lower", "Ne olurdu?!", "ne olurdu"),
    ("colloquial", "Geçen ay alsaydım ne olurdu?", "Geçen ay alsaydım nolurdu?"),
    ("colloquial", "Dün ne yaptım?", "Dün naptım?"),
])
def test_each_transformation_writes_the_same_question_as_people_type_it(name, text, result):
    assert transform(name, text, "seed") == result


def test_a_typo_drops_one_letter_of_a_long_word_and_the_same_seed_drops_the_same_one():
    once, again = transform("typo", "Geçen ay harcamalarımı biriktirseydim", "a"), transform("typo", "Geçen ay harcamalarımı biriktirseydim", "a")
    assert once == again
    assert len(once) == len("Geçen ay harcamalarımı biriktirseydim") - 1
    assert once.split()[:2] == ["Geçen", "ay"]                     # short words keep their letters


def test_turkish_case_keeps_the_dotted_and_dotless_i_apart():
    assert turkish_upper("ılık iş") == "ILIK İŞ"
    assert turkish_lower("ILIK İŞ") == "ılık iş"


# --- Expansion ---------------------------------------------------------------------------------------------------------------

def test_slots_fill_the_text_and_the_expected_arguments_and_the_first_letter_is_capital():
    plain = [v for v in expand(DATA) if v.intent == "summary" and v.transform == "plain"]
    assert [(v.text, v.expected) for v in plain] == [
        ("Geçen ay ne kadar harcadım?", {"tool": "get_spending_summary", "arguments": {"period": "LastMonth"}}),
        ("Dün ne kadar harcadım?", {"tool": "get_spending_summary", "arguments": {"period": "Yesterday"}}),
        ("Ne kadar harcadım?", "clarify")]


def test_template_arguments_go_into_every_alternative():
    variant = next(v for v in expand(DATA) if v.intent == "savings" and v.transform == "plain")
    assert variant.expected == [{"tool": "simulate_savings", "arguments": {"merchant": "Starbucks", "reductionPercent": 50}},
                                {"tool": "simulate_savings", "arguments": {"merchant": "Starbucks", "months": 6, "reductionPercent": 50}}]


def test_every_phrasing_gets_each_transformation_once_and_identical_texts_are_not_repeated():
    variants = expand(DATA)
    ids = [v.id for v in variants]
    assert len(ids) == len(set(ids))
    assert {v.id for v in variants if v.id.startswith("summary.t1.period1.")} == {
        "summary.t1.period1.plain", "summary.t1.period1.ascii", "summary.t1.period1.upper", "summary.t1.period1.lower"}
    # "Dün ne kadar harcadım?" has no "ne olurdu" or "ne yaptım": its colloquial form would be the same text.
    texts = [(v.intent, v.template, v.text) for v in variants]
    assert len(texts) == len(set(texts))


def test_typos_are_only_made_when_asked_and_never_gate():
    assert all(v.transform != "typo" for v in expand(DATA))
    typos = [v for v in expand(DATA, typos=True) if v.transform == "typo"]
    assert typos and all(not v.gating for v in typos)
    assert all(v.gating for v in expand(DATA))


def test_listed_phrasings_and_dialogs_are_expanded_like_the_templates():
    variants = expand(DATA)
    assert {v.id for v in variants if v.intent == "real"} >= {"real.r01.plain", "real.r01.ascii", "real.r01.upper", "real.r01.lower"}
    dialog = next(v for v in variants if v.id == "dialog.d01.upper")
    assert dialog.turns == ("AYDA BİRİKTİRSEYDİM NE OLURDU?", "5000")


def test_a_sample_takes_the_same_phrasings_every_time_and_at_most_the_given_number_per_intent():
    variants = expand(DATA)
    first, again = sample(variants, 2, seed=7), sample(variants, 2, seed=7)
    assert [v.id for v in first] == [v.id for v in again]
    assert max(sum(v.intent == intent for v in first) for intent in {v.intent for v in first}) <= 2
    assert all(v.gating for v in first)
    typos = sample(expand(DATA, typos=True), 1, seed=7, gating=False)
    assert typos and all(v.transform == "typo" for v in typos)


# --- Outcome of a turn -------------------------------------------------------------------------------------------------------

@pytest.mark.parametrize(("reply", "kind"), [
    (ChatReply("clarification", "Hangi dönem için? 1) bu ay", [], []), "clarify"),
    (ChatReply("clarification", CLARIFY_TEXT, [], []), "unclear"),
    (ChatReply("outOfScope", OUT_OF_SCOPE_TEXT, [], []), "out_of_scope"),
    (ChatReply("outOfScope", ADVICE_DECLINED_TEXT, [], []), "advice"),
    (ChatReply("outOfScope", DATE_UNSUPPORTED_TEXT, [], []), "date_unsupported"),
    (ChatReply("answer", LIRA_ONLY_TEXT, [], []), "lira_only"),
    (ChatReply("answer", "Başka bir cevap.", [], []), "answer"),
])
def test_a_reply_is_named_by_what_the_user_sees(reply, kind):
    assert outcome_of(reply) == Outcome(kind)


def test_a_question_back_says_what_it_asks_when_the_waiting_question_is_known():
    reply = ChatReply("clarification", "Tutar hangi para biriminde?", [], [])
    assert outcome_of(reply, "currency") == Outcome("clarify:currency")
    assert matches("clarify", Outcome("clarify:currency")) and not matches("clarify:amount", Outcome("clarify:currency"))


def test_the_amount_question_is_seen_as_such():
    outcome = run_variant(variant("Ayda biriktirseydim ne olurdu?", "clarify:amount", oracle={"tool": "what_if_monthly", "arguments": {}}))
    assert outcome == Outcome("clarify:amount")


def test_a_tool_call_that_reached_the_server_is_the_call():
    assert outcome_of(Captured(ToolCall("get_spending_summary", {"period": "Yesterday"}))) == \
        Outcome("call", "get_spending_summary", {"period": "Yesterday"})


@pytest.mark.parametrize(("expected", "outcome", "ok"), [
    ({"tool": "a", "arguments": {"merchant": "Starbucks"}}, Outcome("call", "a", {"merchant": "STARBUCKS", "x": None}), True),
    ({"tool": "a", "arguments": {}}, Outcome("call", "b", {}), False),
    ({"tool": "a", "arguments": {}}, Outcome("call", "a", {"days": 7}), False),
    ([{"tool": "a", "arguments": {}}, {"tool": "a", "arguments": {"days": 30}}], Outcome("call", "a", {"days": 30}), True),
    ("clarify", Outcome("clarify"), True),
    ("clarify", Outcome("call", "a", {}), False),
    ("advice", Outcome("out_of_scope"), False),
])
def test_an_outcome_matches_only_the_expected_kind_tool_and_arguments(expected, outcome, ok):
    assert matches(expected, outcome) is ok


# --- The oracle: the model choosing right ------------------------------------------------------------------------------------

def tools(*names, enum=("ThisMonth", "LastMonth", "Custom")):
    return [{"type": "function", "function": {"name": n, "parameters": {"properties": {"period": {"enum": list(enum)}}}}} for n in names]


def test_the_oracle_writes_the_expected_call_without_the_range_code_reads_itself():
    september = {"tool": "a", "arguments": {"period": "Custom", "from": "2026-09-01", "to": "2026-09-30"}}
    assert OracleSelector(september).select("Eylül'de?", tools("a"), 0.0) == [ToolCall("a", {"period": "ThisMonth"})]
    # A complete-month tool offers no "this month": the model can only write last month there.
    assert OracleSelector(september).select("Eylül'de?", tools("a", enum=("LastMonth",)), 0.0) == [ToolCall("a", {"period": "LastMonth"})]


def test_the_oracle_chooses_nothing_when_the_route_did_not_show_the_tool_or_no_tool_is_expected():
    assert OracleSelector({"tool": "a", "arguments": {}}).select("x", tools("b"), 0.0) == []
    assert OracleSelector("out_of_scope").select("x", tools("a"), 0.0) == []
    assert OracleSelector([{"tool": "a", "arguments": {"days": 30}}]).select("x", tools("a"), 0.0) == [ToolCall("a", {"days": 30})]


def test_the_oracle_answers_the_first_turn_only():
    oracle = OracleSelector({"tool": "a", "arguments": {}})
    assert oracle.select("x", tools("a"), 0.0) and oracle.select("y", tools("a"), 0.0) == []


def test_the_probe_gateway_lists_the_snapshot_and_stops_at_the_call():
    gateway = ProbeGateway([{"function": {"name": "a"}}])
    assert gateway.list_tools("token", 0.0) == [{"function": {"name": "a"}}]
    with pytest.raises(Captured) as caught:
        gateway.call("a", {"days": 7}, "token", 0.0)
    assert caught.value.call == ToolCall("a", {"days": 7})


# --- The whole chat turn, with the oracle in the model's place ---------------------------------------------------------------

def variant(text, expected, oracle=None, context=None, turns=None):
    return Variant("x.t1.plain", "x", "t1", text, expected, oracle, "plain", True, context, turns or (text,))


def test_a_question_the_code_lets_through_reaches_the_server_with_the_users_words():
    outcome = run_variant(variant("Dün ne harcadım?", {"tool": "get_spending_summary", "arguments": {"period": "Yesterday"}}))
    assert outcome == Outcome("call", "get_spending_summary", {"period": "Yesterday"})


def test_a_date_the_user_wrote_is_read_by_code_with_the_demo_day():
    outcome = run_variant(variant("Eylül'de maaşımın ne kadarını harcadım?",
                                  {"tool": "get_income_share", "arguments": {"period": "Custom", "from": "2026-09-01", "to": "2026-09-30"}}))
    assert outcome == Outcome("call", "get_income_share", {"period": "Custom", "from": "2026-09-01", "to": "2026-09-30"})
    assert DAY.today.isoformat() == "2026-10-10"


def test_a_follow_up_is_read_with_its_context():
    previous = {"tool": "get_spending_advice", "arguments": {"period": "LastMonth"}, "question": "Geçen ay nerede tasarruf edebilirim?"}
    outcome = run_variant(variant("Peki bu tutarı biriktirseydim?", {"tool": "what_if_monthly", "arguments": {"fromAdvice": True}},
                                  context=previous))
    assert outcome == Outcome("call", "what_if_monthly", {"fromAdvice": True})


def test_a_dialog_answers_the_question_asked_back():
    outcome = run_variant(variant("Ne kadar harcadım?", {"tool": "get_spending_summary", "arguments": {"period": "LastMonth"}},
                                  oracle={"tool": "get_spending_summary", "arguments": {"period": "ThisMonth"}},
                                  turns=("Ne kadar harcadım?", "geçen ay")))
    assert outcome == Outcome("call", "get_spending_summary", {"period": "LastMonth"})


def test_the_probe_runs_the_chat_service_itself_with_the_snapshot_and_no_model():
    chat, gateway = probe(OracleSelector("out_of_scope"), load_tools())
    assert chat.answer("Bana bir fıkra anlat", "token", DAY).type == "outOfScope"
    assert gateway.listed == 0                                     # a rule answered: the tools were not even listed


# --- The whole set (K4-B) ----------------------------------------------------------------------------------------------------

def test_the_tool_snapshot_has_the_fifteen_published_tools():
    names = [tool["function"]["name"] for tool in load_tools()]
    assert len(names) == 15 and "what_if_monthly" in names and TOOLS_FILE.name == "mcp_tools.json"


def test_every_phrasing_passes_or_is_a_known_gap_with_a_reason():
    from phrasings import load

    gaps = json.loads(GAPS_FILE.read_text(encoding="utf-8"))["gaps"]
    assert all(isinstance(reason, str) and reason.strip() for reason in gaps.values())
    variants = expand(load())
    assert len(variants) >= 350
    failing = {v.id for v in variants if v.gating and not matches(v.expected, run_variant(v))}
    # Both ways: a new failure is a regression, a gap that now passes must leave the list (and the PR says so).
    assert sorted(failing - gaps.keys()) == [], "new failures"
    assert sorted(gaps.keys() - failing) == [], "fixed gaps still listed"
