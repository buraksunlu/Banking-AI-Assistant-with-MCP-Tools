"""The question asked back when a tool call is missing something, and the user's reply to it (CHAT-03).

K3-A: the question is remembered for one reply, for five minutes, under a digest of the user's token. K4-B: the model may word
the question, but the options are always the backend's closed set or a fixed list, and the reply is matched in code against
the stored options, whatever the model wrote. K5-A: a reply that is not one of them is a new question.
"""

import re
import time
from dataclasses import dataclass

from answer_check import FORBIDDEN_MENTIONS, fold_turkish
from grounding import count_in, period_in
from memory import TokenMemory

# The periods offered when the user named none; any other tool period in the reply counts too (grounding.period_in).
PERIOD_OPTIONS = (("bu ay", "ThisMonth"), ("geçen ay", "LastMonth"), ("son 3 ay", "Last3Months"))
MAX_REPLY_WORDS = 4
# Examples for a count, not numbered: "1" then means one month, not the first option.
MONTH_EXAMPLES = (3, 6, 12)
_ORDINALS = {"birinci": 1, "ilk": 1, "ilki": 1, "ikinci": 2, "ikincisi": 2, "üçüncü": 3, "üçüncüsü": 3, "dördüncü": 4,
             "dördüncüsü": 4, "beşinci": 5, "beşincisi": 5}


@dataclass(frozen=True)
class Option:
    """One choice: how it is written to the user, and the argument value it stands for."""

    label: str
    value: str | int


@dataclass(frozen=True)
class Pending:
    """A tool call waiting for one argument: ``arguments`` holds the rest, ``kind`` is "period", "count" or "merchant"."""

    tool: str
    arguments: dict
    argument: str
    kind: str
    options: tuple[Option, ...]
    question: str
    asked: str = ""
    minimum: int | None = None
    maximum: int | None = None

    @property
    def numbered(self) -> bool:
        """Periods and merchants are numbered choices; counts are examples."""
        return self.kind != "count"


def ask_period(tool: str, arguments: dict, question: str) -> Pending:
    """The question for a period the user did not name."""
    rest = {k: v for k, v in arguments.items() if k != "period"}
    return Pending(tool, rest, "period", "period", tuple(Option(label, value) for label, value in PERIOD_OPTIONS), question)


def ask_count(tool: str, arguments: dict, argument: str, minimum: int, maximum: int, question: str) -> Pending:
    """The question for a count the user did not write, with examples inside the tool's limits."""
    rest = {k: v for k, v in arguments.items() if k != argument}
    examples = tuple(Option(f"son {n} ay", n) for n in MONTH_EXAMPLES if minimum <= n <= maximum)
    return Pending(tool, rest, argument, "count", examples, question, minimum=minimum, maximum=maximum)


def ask_merchant(tool: str, arguments: dict, asked: str, candidates: list[str], question: str) -> Pending:
    """The question for a merchant name that matched several of the customer's merchants (the backend's candidates)."""
    rest = {k: v for k, v in arguments.items() if k != "merchant"}
    return Pending(tool, rest, "merchant", "merchant", tuple(Option(c, c) for c in candidates), question, asked=asked)


def resolve(pending: Pending, reply: str) -> dict | None:
    """The completed arguments when the reply is one of the choices; None when it is something else (a new question)."""
    # A reply is short ("geçen ay", "son 3 ay olsun", "Şehir Ulaşım"); a longer message that happens to name a period or a
    # merchant ("Bu hafta Starbucks'a ne kadar verdim?") is a question of its own and must not lose its other words.
    if len(reply.split()) > MAX_REPLY_WORDS:
        return None
    value = _value(pending, reply)
    return None if value is None else {**pending.arguments, pending.argument: value}


def template(pending: Pending) -> str:
    """The fixed question: what the user sees when the model's wording is not used."""
    if pending.kind == "count":
        numbers = [str(o.value) for o in pending.options]
        return f"Kaç aylık bakayım? Örneğin son {', '.join(numbers[:-1])} ya da {numbers[-1]} ay."
    choices = " ".join(f"{n}) {o.label}" for n, o in enumerate(pending.options, start=1))
    if pending.kind == "merchant":
        return f"‘{pending.asked}’ birden fazla işyeriyle eşleşti: {choices}. Hangisini kastettiniz?"
    return f"Hangi dönem için? {choices}"


def check_question(text: str, pending: Pending) -> bool:
    """True when a written question asks, names every option and adds no other number, choice or figure."""
    # It must ask: in the CHAT-03 trial the model sometimes wrote only the list, or a statement without a question.
    if not text.strip() or len(text) > 400 or "?" not in text:
        return False
    folded = fold_turkish(text)
    if any(word in folded for word in FORBIDDEN_MENTIONS):
        return False
    # A count's examples may be written together ("son 3, 6 ya da 12 ay"), so for counts the number itself must be there.
    if pending.kind == "count":
        if any(not re.search(rf"\b{o.value}\b", text) for o in pending.options):
            return False
    elif any(fold_turkish(o.label) not in folded for o in pending.options):
        return False
    # Every number must be an option's number, part of an option, or the user's own (a choice or a figure cannot be added).
    allowed = {str(n) for n in range(1, len(pending.options) + 1)} if pending.numbered else set()
    for source in (*(o.label for o in pending.options), pending.asked, pending.question):
        allowed.update(re.findall(r"\d+", source))
    return set(re.findall(r"\d+", text)) <= allowed


class PendingStore:
    """At most one waiting question per user, forgotten after ``ttl_seconds`` and used once (memory.TokenMemory)."""

    def __init__(self, ttl_seconds: float = 300.0, capacity: int = 1000, clock=time.monotonic):
        self._memory = TokenMemory(ttl_seconds, capacity, clock)

    def put(self, token: str, pending: Pending) -> None:
        """Remembers the question for this token, replacing an older one."""
        self._memory.put(token, pending)

    def take(self, token: str) -> Pending | None:
        """The waiting question, removed (a reply is used once); None when there is none or it is too old."""
        return self._memory.pop(token)


def _value(pending: Pending, reply: str) -> str | int | None:
    if pending.kind == "count":
        count = count_in(reply)
        return count if count is not None and pending.minimum <= count <= pending.maximum else None
    choice = _choice_number(reply)
    if choice is not None:
        return pending.options[choice - 1].value if 1 <= choice <= len(pending.options) else None
    if pending.kind == "period":
        return period_in(reply)
    return _merchant(pending.options, reply)


def _choice_number(reply: str) -> int | None:
    text = fold_turkish(reply).strip().rstrip(".)")
    if text.isdigit():
        return int(text)
    return _ORDINALS.get(text)


def _merchant(options: tuple[Option, ...], reply: str) -> str | None:
    text = fold_turkish(reply).strip()
    if len(text) < 3:
        return None
    names = {fold_turkish(o.label): o.value for o in options}
    if text in names:
        return names[text]
    # One name inside the reply ("Şehir Ulaşım olsun") or the reply inside one name ("otobüs"); never a choice among several.
    within = [value for name, value in names.items() if name in text] or [value for name, value in names.items() if text in name]
    return within[0] if len(within) == 1 else None
