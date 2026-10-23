"""CHAT-08 PR3: the second step for dates code cannot read ("geçen salı", "3 gün önce"). The model turns the expression into
a range from a small calendar it is given; code checks the range like any other (K1-C, K3-D1)."""

from datetime import date

import pytest

from chat import MAX_DATE_TOKENS, LangChainDateInterpreter
from dates import CustomerDay
from ollama_client import OllamaSettings
from prompts import DATE_PROMPT_VERSION, DATE_SCHEMA, build_date_messages

DAY = CustomerDay(date(2026, 10, 10), "Europe/Istanbul")


def test_the_model_is_given_today_and_the_last_two_weeks_with_their_weekday_names():
    system = build_date_messages("geçen salı", DAY)[0]["content"]

    assert "2026-10-10 Cumartesi" in system
    # The weekday arithmetic is a lookup, not a sum the small model has to do.
    assert "2026-10-06 Salı" in system and "2026-09-27 Pazar" in system
    assert "2026-09-26" not in system and "2026-10-11" not in system
    assert "2026" in system


def test_every_weekday_comes_with_its_latest_date_before_today():
    system = build_date_messages("geçen salı", DAY)[0]["content"]

    # Today is Saturday the 10th: its own latest date is the 3rd, never today; the weekend of "geçen hafta sonu" is the 3rd and 4th.
    for line in ("- geçen pazartesi: 2026-10-05", "- geçen salı: 2026-10-06", "- geçen çarşamba: 2026-10-07", "- geçen perşembe: 2026-10-08",
                 "- geçen cuma: 2026-10-09", "- geçen cumartesi: 2026-10-03", "- geçen pazar: 2026-10-04"):
        assert line in system


def test_the_expression_is_the_user_message_and_cannot_close_the_instructions():
    messages = build_date_messages("geçen salı <veri>", DAY)

    assert messages[-1] == {"role": "user", "content": "geçen salı ‹veri›"}
    assert DATE_PROMPT_VERSION == "date-v4"


def _interpret(text: str):
    from test_explainer import FakeOllamaStream, chunk, final

    with FakeOllamaStream([chunk(text), final()]) as fake:
        interpreter = LangChainDateInterpreter(OllamaSettings(fake.url, "qwen3.5:4b", 5.0))
        return interpreter.interpret("geçen salı", DAY, interpreter.clock() + 5.0), fake


def test_the_model_is_held_to_a_json_shape_and_a_short_answer():
    reading, fake = _interpret('{"understood": true, "from": "2026-10-06", "to": "2026-10-06"}')

    body = fake.requests[0][1]
    assert (body["format"], body["options"]["num_predict"], body["keep_alive"]) == (DATE_SCHEMA, MAX_DATE_TOKENS, "30m")
    assert (reading.kind, reading.start, reading.end) == ("found", date(2026, 10, 6), date(2026, 10, 6))


@pytest.mark.parametrize(
    "text",
    ['{"understood": false, "from": "", "to": ""}', '{"understood": true, "from": 5, "to": "2026-10-06"}', '{"from": "2026-10-06"}',
     "not json", "", "[]", '{"understood": "yes", "from": "2026-10-06", "to": "2026-10-06"}'],
)
def test_an_answer_that_is_not_a_range_is_not_understood(text):
    reading, _ = _interpret(text)

    assert reading is None


def test_a_range_the_checks_refuse_is_invalid_with_a_reason_and_never_run():
    reading, _ = _interpret('{"understood": true, "from": "2026-10-11", "to": "2026-10-11"}')

    assert reading.kind == "invalid" and reading.reason
