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

# CHAT-09 K11-A: the six Turkish letters as plain ones, for the text (fold_turkish) and for the rules (fold_rule).
_PLAIN = str.maketrans("çğıöşü", "cgiosu")


def fold_rule(pattern: str) -> str:
    """A rule pattern written in Turkish (lower case), folded like the text it is matched against."""
    return pattern.translate(_PLAIN)


# Thousands-separated form first; otherwise "1.840,00" would be read as 1 and 840,00. No sign: "-415,00" and
# "415,00 TL azaldı" are the same fact.
_TURKISH_NUMBER = re.compile(r"\d{1,3}(?:\.\d{3})+(?:,\d+)?|\d+(?:,\d+)?")


# INC-03 K7-A: wording that tells the reader what to buy or what a price will do. The what-if answers describe the past; a model
# answer with any of these is replaced by the facts. A closed list: it catches the usual phrasings, not every possible one.
_ADVICE = re.compile(fold_rule(
    r"\balmali\b|\balmalisiniz|\balin\b|\balabilirsiniz|tavsiye\s+ed|\byapmali|\böner(?:irim|iriz|ilir)\b"
    r"|\byüksel(?:ecek|ir)\b|\bdüş(?:ecek|er)\b|\bartacak|\bkazandir(?:ir|abilir)|\bdeğerlen(?:ecek|ir)\b|\bgetirir\b"
    r"|\bmantikli\b|\bfirsat"))


# A sentence ends at . ! ? before a space or the end, or at a line break; the dot inside "66.494,58" does not end one.
_SENTENCE_END = re.compile(r"[.!?](?=\s|$)|\n")


def misnamed(answer: str, named_values: tuple[tuple[str, str], ...]) -> bool:
    """True when a what-if value stands next to another asset's name (CHAT-09 K16-A).

    ``named_values`` pairs each asset's folded word ("dolar", "euro", "altin") with its value as the facts write it. The asset of a
    value is the nearest name before it in its sentence, or the nearest after it when none comes before. The simulator's answer
    named the three assets and then their values "sırasıyla": every value then sits after "altın", and the reader has to guess.
    """
    folded = fold_turkish(answer)
    words = [word for word, _ in named_values]
    for word, value in named_values:
        for found in re.finditer(re.escape(value), folded):
            start = max((m.end() for m in _SENTENCE_END.finditer(folded, 0, found.start())), default=0)
            end = next((m.start() for m in _SENTENCE_END.finditer(folded, found.end())), len(folded))
            before = [(m.start(), w) for w in words for m in re.finditer(rf"\b{w}", folded[start:found.start()])]
            after = [(m.start(), w) for w in words for m in re.finditer(rf"\b{w}", folded[found.end():end])]
            nearest = max(before)[1] if before else min(after)[1] if after else None
            if nearest is not None and nearest != word:
                return True
    return False


def has_advice(answer: str) -> bool:
    """True when the answer reads as advice to buy or as a forecast ("almalısınız", "yükselecek", "tavsiye ederim")."""
    return _ADVICE.search(fold_turkish(answer)) is not None


class AnswerRejected(Exception):
    """The model's answer failed the check twice; the caller shows the computed result without an explanation."""


def parse_turkish_numbers(text: str) -> list[Decimal]:
    """Returns every number written in Turkish format ("1.840,00", "%29,17", "2026") in order of appearance."""
    return [value for _, value in _numbers_with_text(text)]


def fold_turkish(text: str) -> str:
    """Lower case with every Turkish letter as its plain one, so "TALİMAT", "talımat" and "Dün"/"dun" compare equal.

    CHAT-09 K11-A: people type without the Turkish letters ("Dun ne kadar harcadim"); every rule reads folded text and is folded
    itself (``fold_rule``), so both spellings meet in the middle.
    """
    # "İ" goes first because str.lower() would turn it into "i" plus a combining dot; "I" already lowers to "i".
    return text.replace("İ", "i").lower().translate(_PLAIN)



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
