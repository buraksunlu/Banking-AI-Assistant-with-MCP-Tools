"""Checks that the arguments the model filled in come from the user's own words (CHAT-03, decision K2-A).

The input side of the answer check (AI-06): there every number in the answer must be in the facts, here every period and
count in a tool call must be in the message. A tool's period is required, so the model always writes one; "Ne kadar
harcadım?" became "this month" without the user knowing. Standard library only, deterministic: a phrase the word lists
miss leads to one unnecessary question, never to a guessed figure.
"""

import re
from dataclasses import dataclass

from answer_check import fold_turkish

# Any expression of time. Folded text (fold_turkish): every i-like letter is "i", the other Turkish letters stay.
_TIME = re.compile(
    r"\bbugün|\bdün"                               # bugün, dün, dünkü
    r"|\bhafta"                                     # hafta, haftanın, haftada
    r"|\bay(?:da|in|i|dan|ki|a)?\b"                 # ay, ayın, ayda, ayki; not "aylık" or "aylara" (no period in them)
    r"|\b(?:yil|sene)"                              # yıl, yılın, sene, senenin
    r"|\b\d+\s*gün|\bgünlük"                        # 30 gün, son 7 günde
    r"|\b(?:ocak|şubat|mart\b|marti|nisan|mayis|haziran|temmuz|ağustos|eylül|ekim|kasim|aralik)"  # month names, "mart" not "market"
)

_NUMBER_WORDS = {"bir": 1, "iki": 2, "üç": 3, "dört": 4, "beş": 5, "alti": 6, "yedi": 7, "sekiz": 8, "dokuz": 9, "on": 10,
                 "yirmi": 20, "otuz": 30}

# The tools' own periods (ToolPeriod), each with the words that name it; the order matters ("son 30 gün" before "gün").
_PERIODS = [
    ("Last30Days", r"\bson\s+(?:30|otuz)\s+gün"),
    ("Last7Days", r"\bson\s+(?:7|yedi)\s+gün"),
    ("Last3Months", r"\bson\s+(?:3|üç)\s+ay"),
    ("Last6Months", r"\bson\s+(?:6|alti)\s+ay"),
    ("ThisWeek", r"\bbu\s+hafta"),
    ("LastWeek", r"\bgeçen\s+hafta"),
    ("ThisMonth", r"\bbu\s+ay(?:da|in|i|ki)?\b"),
    ("LastMonth", r"\bgeçen\s+ay(?:da|in|i|ki)?\b"),
    ("ThisYear", r"\bbu\s+(?:yil|sene)"),
    ("Today", r"\bbugün"),
    ("Yesterday", r"\bdün"),
]


@dataclass(frozen=True)
class Grounding:
    """``missing``: the first required argument the user did not give (asked back); ``arguments``: what may run, with
    optional arguments the user did not give left out, so the backend's own default applies."""

    missing: str | None
    arguments: dict


def mentions_period(message: str) -> bool:
    """True when the message says when: a day, week, month, year or month name."""
    return _TIME.search(fold_turkish(message)) is not None


def period_in(reply: str) -> str | None:
    """The tool period a reply names ("geçen ay" → "LastMonth"); None for anything else, never the nearest one."""
    folded = fold_turkish(reply)
    return next((name for name, pattern in _PERIODS if re.search(pattern, folded)), None)


def mentions_count(message: str, value: int) -> bool:
    """True when the message writes this count, in digits or words ("6", "altı"); "yıl" counts as 12."""
    folded = fold_turkish(message)
    if value in _counts(folded):
        return True
    return value == 12 and re.search(r"\b(?:yil|sene)", folded) is not None


def count_in(reply: str) -> int | None:
    """The first count a reply writes, in digits or words; None when there is none."""
    counts = _counts(fold_turkish(reply))
    return counts[0] if counts else None


def check_arguments(arguments: dict, schema: dict, message: str) -> Grounding:
    """Compares a tool call with the message, using the tool's published schema (which arguments are counts, which required)."""
    properties, required = schema.get("properties", {}), set(schema.get("required", []))
    kept = dict(arguments)
    for name, value in arguments.items():
        if _grounded(name, value, properties.get(name, {}), message):
            continue
        if name in required:
            return Grounding(name, dict(arguments))
        del kept[name]
    return Grounding(None, kept)


def _grounded(name: str, value, rule: dict, message: str) -> bool:
    if name == "period":
        return mentions_period(message)
    if rule.get("type") == "integer":
        return isinstance(value, int) and mentions_count(message, value)
    # Merchant names are matched by the backend against the customer's own merchants (MerchantMatcher), not here.
    return True


def _counts(folded: str) -> list[int]:
    words = re.findall(r"\d+|[a-zçğöşü]+", folded)
    found, skip = [], False
    for word, following in zip(words, [*words[1:], ""]):
        if skip:
            skip = False
        elif word.isdigit():
            found.append(int(word))
        elif word in _NUMBER_WORDS:
            # "on iki" is 12; "on" alone is 10.
            if word == "on" and _NUMBER_WORDS.get(following, 10) < 10:
                found.append(10 + _NUMBER_WORDS[following])
                skip = True
            else:
                found.append(_NUMBER_WORDS[word])
    return found
