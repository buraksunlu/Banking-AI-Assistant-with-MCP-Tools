"""Deterministic scoring for the local model check (AI-01).

The model's output is never trusted: every score is computed here by plain code, never by asking a model.
"""

import json
import re
from dataclasses import dataclass
from decimal import Decimal, InvalidOperation

# Thousands-separated form first; otherwise "1.840,00" would be read as 1 and 840,00.
_TURKISH_NUMBER = re.compile(r"\d{1,3}(?:\.\d{3})+(?:,\d+)?|\d+(?:,\d+)?")


@dataclass(frozen=True)
class Score:
    """Result of one scored case: whether it passed and a short reason when it did not."""

    passed: bool
    detail: str = ""


def parse_turkish_numbers(text: str) -> list[Decimal]:
    """Returns every number written in Turkish format ("1.840,00", "%29,17", "2026") in order of appearance."""
    return [Decimal(match.replace(".", "").replace(",", ".")) for match in _TURKISH_NUMBER.findall(text)]


def score_tool_call(expected: dict, tool_calls: list[dict] | None) -> Score:
    """Passes when the model made exactly the expected call (or no call when ``expected["tool"]`` is None)."""
    calls = tool_calls or []
    names = [c["function"]["name"] for c in calls]

    if expected["tool"] is None:
        return Score(True) if not calls else Score(False, f"expected no call, got {names}")

    if len(calls) != 1:
        return Score(False, f"expected 1 call to {expected['tool']}, got {names or 'none'}")

    function = calls[0]["function"]
    if function["name"] != expected["tool"]:
        return Score(False, f"expected {expected['tool']}, got {function['name']}")

    arguments = function.get("arguments") or {}
    if isinstance(arguments, str):
        try:
            arguments = json.loads(arguments)
        except json.JSONDecodeError:
            return Score(False, f"arguments are not JSON: {arguments!r}")

    return _compare_fields(expected.get("args", {}), arguments)


def score_structured(expected: dict, content: str) -> Score:
    """Passes when ``content`` is a JSON object with exactly the expected fields and values."""
    try:
        value = json.loads(content)
    except json.JSONDecodeError:
        return Score(False, f"not valid JSON: {content[:80]!r}")

    if not isinstance(value, dict):
        return Score(False, f"JSON is not an object: {content[:80]!r}")

    return _compare_fields(expected, value)


def score_explanation(source_texts: list[str], must_include: list[str], content: str) -> Score:
    """Passes when every number in ``content`` appears in the given data and every required text is quoted."""
    if not content.strip():
        return Score(False, "empty answer")

    allowed = {number for text in source_texts for number in parse_turkish_numbers(text)}
    invented = [str(n) for n in parse_turkish_numbers(content) if n not in allowed]
    missing = [text for text in must_include if text not in content]

    problems = []
    if invented:
        problems.append(f"numbers not in the data: {', '.join(invented)}")
    if missing:
        problems.append(f"missing text: {', '.join(missing)}")
    return Score(not problems, "; ".join(problems))


def _compare_fields(expected: dict, actual: dict) -> Score:
    problems = []
    for key, value in expected.items():
        if key not in actual:
            problems.append(f"missing {key}")
        elif _normalize(actual[key]) != _normalize(value):
            problems.append(f"{key}={actual[key]!r}, expected {value!r}")
    # An argument the user never asked for is the model guessing, which is what this check must catch.
    problems.extend(f"unexpected {key}={actual[key]!r}" for key in actual if key not in expected)
    return Score(not problems, "; ".join(problems))


def _normalize(value):
    # 5000, 5000.0 and "5000" are the same amount; JSON gives no guarantee which one the model sends.
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float, str)):
        try:
            return Decimal(str(value).strip())
        except InvalidOperation:
            return str(value).strip()
    return value


def count_sentences(text: str) -> int:
    """Counts sentences by their ending punctuation; "12.345,67" is not a sentence end."""
    text = text.strip()
    if not text:
        return 0
    # A run of . ! ? … counts once, and only when followed by whitespace or the end ("12.345" has a digit after the dot).
    ends = len(re.findall(r"[.!?…]+(?=\s|$)", text))
    return ends if re.search(r"[.!?…]$", text) else ends + 1


def score_text_rules(content: str, must_include_any: list[str], must_not_include: list[str], max_sentences: int) -> Score:
    """Passes when one of ``must_include_any`` appears, none of ``must_not_include`` does, and the answer is short enough.

    Text is compared with Turkish casing folded, so "TALIMAT", "TALİMAT" and "talımat" all match "talimat".
    """
    folded = _fold(content)
    problems = []
    if must_include_any and not any(_fold(text) in folded for text in must_include_any):
        problems.append(f"none of: {', '.join(must_include_any)}")
    forbidden = [text for text in must_not_include if _fold(text) in folded]
    if forbidden:
        problems.append(f"forbidden: {', '.join(forbidden)}")
    sentences = count_sentences(content)
    if sentences > max_sentences:
        problems.append(f"{sentences} sentences (max {max_sentences})")
    return Score(not problems, "; ".join(problems))


def _fold(text: str) -> str:
    # Models also write Turkish words in ASCII ("TALIMAT"); fold every i-like letter to "i". "İ" goes first because
    # str.lower() would turn it into "i" plus a combining dot; "I" already lowers to "i".
    return text.replace("İ", "i").lower().replace("ı", "i")
