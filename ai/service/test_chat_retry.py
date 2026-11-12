"""CHAT-10 PR1: what the INC-04 simulator trial found.

"5k" was read as a 5 lira budget, the answer with that budget failed the number check (502), and "Tekrar dene" sent the same
"1" again after the waiting question had been used up: the bare "1" became a new question and the choice of calculation was
asked from the start, which looked like a loop. Decisions: K9-C (units after a number are read, an unknown letter is asked back),
K10-A (a turn that fails gives back the waiting question and the offered follow-ups), K10-D (a bare choice with nothing waiting
is not sent to the model).
"""

from answer_check import AnswerRejected
from chat import ChatReply, ToolCall
from prompts import BARE_CHOICE_TEXT
from routing import Router
from tools import BackendTimeout
from test_chat import (AMOUNT_CHOICES, AMOUNT_HINT, AMOUNT_QUESTION, CURRENCY_CHOICES, BUDGET_RESULT, CURRENCY_QUESTION, SUMMARY_YESTERDAY, TOKEN, YESTERDAY_TWO, YESTERDAY_CATEGORIES,
                       FakeExplainer, FakeGateway, service)
from test_chat_wealth import DAY
from test_clarification import TOPIC_TEXT

import pytest


class FailingOnce(FakeExplainer):
    """Rejects the first explanation it is asked for, like the live 502 (invented numbers), then explains as usual."""

    def __init__(self, fail_at=1):
        super().__init__()
        self.asked, self.fail_at = 0, fail_at

    def explain(self, question, facts, deadline=None):
        self.asked += 1
        if self.asked == self.fail_at:
            raise AnswerRejected("Rejected after one correction")
        return super().explain(question, facts, deadline)


class Planning:
    """The intent classifier of the live run, which put a bare "1" into the planning group."""

    def classify(self, message, deadline):
        return "planning"


class TimingOutOnce(FakeGateway):
    """Times out on the first call, then answers. Since CHAT-10 PR3b D13-A the budget answer is written by code, so the turn of the
    trial that failed (the 502 of the live run) now fails at the backend instead of at the explanation; the put-back is the same."""

    def call(self, tool, arguments, token, deadline):
        if not self.calls:
            self.calls.append((tool, arguments, token, deadline))
            raise BackendTimeout("first call times out")
        return super().call(tool, arguments, token, deadline)


def trial(explainer=None):
    gateway = FakeGateway(BUDGET_RESULT) if explainer is not None else TimingOutOnce(BUDGET_RESULT)
    chat, selector, gateway, explainer = service([], gateway=gateway, explainer=explainer or FakeExplainer())
    chat.router = Router(Planning())
    return chat, selector, gateway, explainer


# --- the trial, step by step ---------------------------------------------------------------------------------------------------

def test_the_trial_reads_5k_as_five_thousand():
    chat, _, gateway, _ = trial(explainer=FakeExplainer())
    chat.answer("Bütçemi kısıp tasarruf etsem?", TOKEN, DAY)
    chat.answer("1", TOKEN, DAY)

    asked = chat.answer("5k", TOKEN, DAY)
    reply = chat.answer("1", TOKEN, DAY)

    assert asked == ChatReply("clarification", CURRENCY_QUESTION, [], [], choices=CURRENCY_CHOICES)
    assert reply.type == "answer"
    assert gateway.calls[0][:2] == ("check_budget", {"amount": 5000, "currencyCode": "TRY"})


def test_retrying_a_failed_reply_completes_the_same_call_and_asks_nothing_again():
    chat, _, gateway, _ = trial()
    assert chat.answer("Bütçemi kısıp tasarruf etsem?", TOKEN, DAY).answer == TOPIC_TEXT
    assert chat.answer("1", TOKEN, DAY).answer == AMOUNT_QUESTION
    chat.answer("5k", TOKEN, DAY)
    with pytest.raises(BackendTimeout):
        chat.answer("1", TOKEN, DAY)

    retried = chat.answer("1", TOKEN, DAY)

    assert retried.type == "answer" and retried.answer != TOPIC_TEXT
    budget = ("check_budget", {"amount": 5000, "currencyCode": "TRY"})
    assert [call[:2] for call in gateway.calls] == [budget, budget]


def test_the_waiting_question_given_back_is_still_used_once():
    chat, _, _, _ = trial()
    chat.answer("Bütçemi kısıp tasarruf etsem?", TOKEN, DAY)
    chat.answer("1", TOKEN, DAY)
    chat.answer("5k", TOKEN, DAY)
    with pytest.raises(BackendTimeout):
        chat.answer("1", TOKEN, DAY)
    chat.answer("1", TOKEN, DAY)

    # After the answer nothing waits any more: a third "1" is no reply to anything.
    assert chat.answer("1", TOKEN, DAY) == ChatReply("clarification", BARE_CHOICE_TEXT, [], [])


def test_retrying_a_failed_offered_follow_up_runs_it_without_the_model():
    gateway = FakeGateway([YESTERDAY_TWO, YESTERDAY_CATEGORIES, YESTERDAY_CATEGORIES])
    chat, selector, gateway, _ = service([SUMMARY_YESTERDAY], gateway=gateway, explainer=FailingOnce(fail_at=2))
    chat.answer("Dün ne harcadım?", TOKEN)
    with pytest.raises(AnswerRejected):
        chat.answer("Dün kategorilere göre harcamam nasıl?", TOKEN)

    retried = chat.answer("Dün kategorilere göre harcamam nasıl?", TOKEN)

    assert retried.tool_calls == [ToolCall("get_category_breakdown", {"period": "Yesterday"})]
    assert len(selector.calls) == 1


# --- K10-D: a bare choice with nothing waiting ---------------------------------------------------------------------------------

@pytest.mark.parametrize("message", ["1", "2", " 3 ", "ikincisi", "ilk"])
def test_a_bare_choice_with_nothing_waiting_is_not_sent_to_the_model(message):
    chat, selector, gateway, _ = trial()

    reply = chat.answer(message, TOKEN, DAY)

    assert reply == ChatReply("clarification", BARE_CHOICE_TEXT, [], [])
    assert selector.calls == [] and gateway.list_calls == [] and gateway.calls == []


def test_a_bare_choice_that_is_an_offered_follow_up_is_still_that_follow_up():
    # Nothing in the table is a bare number today; the offer is looked at first all the same, as before K10-D.
    chat, selector, _, _ = service([SUMMARY_YESTERDAY], gateway=FakeGateway([YESTERDAY_TWO, YESTERDAY_CATEGORIES]))
    chat.answer("Dün ne harcadım?", TOKEN)

    assert chat.answer("Dün kategorilere göre harcamam nasıl?", TOKEN).type == "answer" and len(selector.calls) == 1


# --- K9-C: an amount the code cannot read --------------------------------------------------------------------------------------

def test_an_amount_reply_the_code_cannot_read_is_asked_again():
    chat, _, gateway, _ = trial(explainer=FakeExplainer())
    chat.answer("Bütçemi kısıp tasarruf etsem?", TOKEN, DAY)
    chat.answer("1", TOKEN, DAY)

    again = chat.answer("5x", TOKEN, DAY)
    chat.answer("5000 TL", TOKEN, DAY)

    assert again == ChatReply("clarification", "‘5x’ tutarını anlayamadım. " + AMOUNT_QUESTION, [], [], choices=AMOUNT_CHOICES, input_hint=AMOUNT_HINT)
    assert gateway.calls[0][:2] == ("check_budget", {"amount": 5000, "currencyCode": "TRY"})


def test_a_question_with_an_amount_the_code_cannot_read_asks_the_amount():
    # The model read "5x" as 5; that figure is not the user's, so it is asked (the check that was fooled by "5k" before K9-C).
    chat, _, gateway, _ = service([ToolCall("check_budget", {"amount": 5, "currencyCode": "TRY"})],
                                  gateway=FakeGateway(BUDGET_RESULT))

    reply = chat.answer("Bu ay 5x TL bütçemi aşar mıyım?", TOKEN)

    assert reply == ChatReply("clarification", AMOUNT_QUESTION, [], [], choices=AMOUNT_CHOICES, input_hint=AMOUNT_HINT) and gateway.calls == []
