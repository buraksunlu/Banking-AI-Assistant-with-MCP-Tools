"""Checks a model answer against the facts it was given, before it reaches the user (AI-06).

Standard library only: ai/model_check/scoring.py imports the number reading from here, and run.py there runs
without the service's packages.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from decimal import Decimal
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    # Only for type hints: importing contracts would pull in pydantic, which run.py does not have.
    from contracts import Fact

# Words that only appear when the model talks about the system instead of the user's money (AI-05 eval, X09).
FORBIDDEN_MENTIONS = ("<veri", "</veri", "backend")

# Thousands-separated form first; otherwise "1.840,00" would be read as 1 and 840,00. No sign: "-415,00" and
# "415,00 TL azaldı" are the same fact.
_TURKISH_NUMBER = re.compile(r"\d{1,3}(?:\.\d{3})+(?:,\d+)?|\d+(?:,\d+)?")


class AnswerRejected(Exception):
    """The model's answer failed the check twice; the caller shows the computed result without an explanation."""


def parse_turkish_numbers(text: str) -> list[Decimal]:
    """Returns every number written in Turkish format ("1.840,00", "%29,17", "2026") in order of appearance."""
    return [value for _, value in _numbers_with_text(text)]


def fold_turkish(text: str) -> str:
    """Lower case with every i-like letter as "i", so "TALIMAT", "TALİMAT" and "talımat" compare equal."""
    # "İ" goes first because str.lower() would turn it into "i" plus a combining dot; "I" already lowers to "i".
    return text.replace("İ", "i").lower().replace("ı", "i")


@dataclass(frozen=True)
class AnswerCheck:
    """What is wrong with an answer; an answer with no problem is ``ok``."""

    empty: bool = False
    invented_numbers: tuple[str, ...] = ()
    forbidden_mentions: tuple[str, ...] = ()

    @property
    def ok(self) -> bool:
        """True when nothing is wrong."""
        return not (self.empty or self.invented_numbers or self.forbidden_mentions)

    def describe(self) -> str:
        """A log line with counts only: the numbers themselves are financial data and stay out of logs."""
        return (f"empty: {'yes' if self.empty else 'no'}, invented numbers: {len(self.invented_numbers)}, "
                f"forbidden mentions: {len(self.forbidden_mentions)}")


def check_answer(question: str, facts: list[Fact], answer: str) -> AnswerCheck:
    """Every number in the answer must appear in the question or the facts; the answer must not be empty or talk
    about the system behind it."""
    if not answer.strip():
        return AnswerCheck(empty=True)

    sources = [question, *(f"{fact.label} {fact.text}" for fact in facts)]
    allowed = {value for source in sources for value in parse_turkish_numbers(source)}

    # Compared by value, so "735" matches "735,00"; reported as written, so the correction quotes the model.
    invented = [text for text, value in _numbers_with_text(answer) if value not in allowed]
    folded = fold_turkish(answer)
    mentions = [word for word in FORBIDDEN_MENTIONS if word in folded]

    return AnswerCheck(invented_numbers=tuple(dict.fromkeys(invented)), forbidden_mentions=tuple(mentions))


def _numbers_with_text(text: str) -> list[tuple[str, Decimal]]:
    return [(match, Decimal(match.replace(".", "").replace(",", "."))) for match in _TURKISH_NUMBER.findall(text)]
