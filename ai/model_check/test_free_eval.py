"""CHAT-12 PR1: the free-phrasing measurement. Nothing in the chat changes here: the test locks where each sentence ends today, so
PR2 and PR3 show what they moved (a better bucket has to be written into the baseline, a worse one breaks the test)."""

import json

import pytest

from free_eval import (ASKED, BASELINE_FILE, FREE_FILE, RIGHT, UNANSWERED, WRONG, all_cases, bucket, run_case, shuffled,
                       summary)
from phrasings import Outcome

CALL = {"tool": "get_accounts", "arguments": {}}


# --- The four buckets (K8-A: "wrong" is the dangerous one) -------------------------------------------------------------------

@pytest.mark.parametrize("expected, outcome, shown, result", [
    (CALL, Outcome("call", "get_accounts", {}), [["get_accounts"]], RIGHT),
    (CALL, Outcome("call", "get_spending_summary", {"period": "ThisMonth"}), [["get_spending_summary"]], WRONG),
    (CALL, Outcome("clarify:period"), [["get_accounts"]], ASKED),
    (CALL, Outcome("unclear"), [], ASKED),
    (CALL, Outcome("out_of_scope"), [], UNANSWERED),
    # The model-free oracle chooses nothing when the right tool is not shown, so the turn looks unanswered; a real model would
    # have answered from the wrong list. That door is counted wrong.
    (CALL, Outcome("out_of_scope"), [["get_spending_summary", "get_category_breakdown"]], WRONG),
    (CALL, Outcome("clarify:period"), [["get_spending_summary"]], WRONG),
    # A question that must not get a figure: any safe reply is right, a call is wrong.
    (["out_of_scope", "clarify"], Outcome("out_of_scope"), [["get_spending_summary"]], RIGHT),
    (["out_of_scope", "clarify"], Outcome("call", "get_spending_summary", {"period": "ThisMonth"}), [["get_spending_summary"]], WRONG),
    ("clarify:period", Outcome("call", "get_spending_summary", {"period": "ThisMonth"}), [["get_spending_summary"]], WRONG),
])
def test_each_end_of_a_turn_has_one_bucket(expected, outcome, shown, result):
    assert bucket(expected, outcome, shown, modelless=True) == result


def test_with_the_model_only_what_happened_counts():
    # The real model answered nothing from the wrong list: unanswered, not wrong.
    assert bucket(CALL, Outcome("out_of_scope"), [["get_spending_summary"]], modelless=False) == UNANSWERED


# --- The set and the probe --------------------------------------------------------------------------------------------------

def test_the_set_has_eighty_sentences_with_kinds_and_room_for_the_users_own():
    data = json.loads(FREE_FILE.read_text(encoding="utf-8"))
    assert len(data["cases"]) == 80 and isinstance(data["burak"], list)
    assert len({case["id"] for case in all_cases(data)}) == len(all_cases(data))
    assert all(case["kind"] for case in data["cases"])


def test_a_case_runs_through_the_chat_service_itself_and_records_the_tools_shown():
    outcome, shown = run_case("Bakiyem ne kadar?", CALL)
    # The balance needs no model (ACC-01b K2-C): the call is made by code, the model was shown nothing.
    assert (outcome, shown) == (Outcome("call", "get_accounts", {}), [])

    # A phrasing no rule knows goes to the general group without a model (CHAT-12 PR3 gave "elimde ne kadar para var" its rule).
    outcome, shown = run_case("cüzdanımda ne kadar var", CALL)
    assert outcome.kind == "out_of_scope" and "get_accounts" not in shown[0]


def test_where_every_sentence_ends_today_is_the_baseline():
    # Both ways, like phrasing_gaps.json: a sentence that moved (better or worse) must be written into the baseline, and the PR
    # shows the change. Model-free; the model's run is free_eval.py --model.
    baseline = json.loads(BASELINE_FILE.read_text(encoding="utf-8"))["buckets"]
    now = {case["id"]: bucket(case["expected"], *run_case(case["text"], case["expected"]), modelless=True)
           for case in all_cases(json.loads(FREE_FILE.read_text(encoding="utf-8")))}
    assert now == baseline


def test_the_summary_counts_each_bucket_and_the_wrong_share():
    counts = summary([RIGHT, RIGHT, ASKED, WRONG])
    assert counts == {RIGHT: 2, ASKED: 1, UNANSWERED: 0, WRONG: 1, "total": 4, "wrongShare": 25.0}


# --- Word order (K2-C: report only) -----------------------------------------------------------------------------------------

def test_a_shuffle_keeps_every_word_and_the_question_mark_and_is_the_same_each_run():
    first = shuffled([("x", "Geçen ay ne kadar harcadım?", CALL)], seed=1)
    assert first == shuffled([("x", "Geçen ay ne kadar harcadım?", CALL)], seed=1)
    (_, text, _), = first
    assert text.endswith("?") and sorted(text.rstrip("?").lower().split()) == sorted("geçen ay ne kadar harcadım".split())
