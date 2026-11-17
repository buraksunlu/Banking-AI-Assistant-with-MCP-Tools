"""CHAT-13 PR1 K7-A: a reply to the cut question that writes an amount or a duration is no percent. Found while reading the typo report:
"Tasarruf etsem ne olur?" → "Ne kadar azaltmayı düşünüyorsunuz?" → "5 bin lira" simulated a 5 % cut, "1.000" a 1 % cut and "2 yıl" a
2 % cut; the first digit of the reply was taken for the cut. Asked again instead, saying why."""

import pytest

from clarification import ask_percent, not_a_percent, resolve, template
from test_chat import TOKEN, service

PERCENT = ask_percent("simulate_savings", {}, "Tasarruf etsem ne olur?")
AGAIN = "Bunu yüzde olarak okuyamadım. Ne kadar azaltmayı düşünüyorsunuz?"


@pytest.mark.parametrize("reply", ["5 bin lira", "1.000", "2 yıl", "6 ay", "500 TL", "5k", "100 dolar", "2.000 TL azaltsam"])
def test_an_amount_or_a_duration_is_no_cut(reply):
    assert resolve(PERCENT, reply) is None
    assert not_a_percent(PERCENT, reply)


@pytest.mark.parametrize("reply, cut", [("%20", 20), ("20", 20), ("yüzde 15", 15), ("yarısı", 50), ("20%", 20),
                                        # A percent sign or word decides even beside a duration: the duration is the user's own.
                                        ("%10 6 ay boyunca", 10)])
def test_a_cut_is_still_read(reply, cut):
    assert resolve(PERCENT, reply) == {"reductionPercent": cut}
    assert not not_a_percent(PERCENT, reply)


@pytest.mark.parametrize("reply", ["Bu ay ne kadar harcadım?", "geçen ay", "150"])
def test_other_replies_are_not_this_case(reply):
    # A new question goes its own way, and a count past 100 is the existing range check's, as before.
    assert not not_a_percent(PERCENT, reply)


def test_only_the_cut_question_is_concerned():
    from clarification import ask_amount
    assert not not_a_percent(ask_amount("check_budget", {}, "Bütçem?"), "5 bin lira")


@pytest.mark.parametrize("reply", ["5 bin lira", "1.000", "2 yıl"])
def test_the_chat_asks_the_cut_again_and_runs_nothing(reply):
    chat, _, gateway, _ = service([])

    asked = chat.answer("Tasarruf etsem ne olur?", TOKEN)
    again = chat.answer(reply, TOKEN)

    assert asked.answer == template(PERCENT)
    assert (again.type, again.answer, gateway.calls) == ("clarification", AGAIN, [])


def test_after_asking_again_a_cut_completes_it():
    chat, _, gateway, _ = service([])

    chat.answer("Tasarruf etsem ne olur?", TOKEN)
    chat.answer("5 bin lira", TOKEN)
    try:
        chat.answer("%20", TOKEN)
    except KeyError:
        pass   # the fake gateway's answer is no savings result; only the call that reached it matters here

    assert [c[:2] for c in gateway.calls] == [("simulate_savings", {"reductionPercent": 20})]
