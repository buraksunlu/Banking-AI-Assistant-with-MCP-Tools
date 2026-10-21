"""The last call of a conversation, the context of a short follow-up ("Peki geçen ay?") (CHAT-02).

K2-A: only the last successful call is kept (tool, arguments and the question), for ten minutes after it ran; the answer's
figures are never kept, so a follow-up is always computed again by the backend. K4-B: only short messages, or ones that start
like a follow-up, are given the context; a full question stands on its own, as before.
"""

import re
import time
from dataclasses import dataclass

from answer_check import fold_turkish
from memory import TokenMemory

# A full question is usually five words or more ("Bu ay ne kadar harcadım?"); a follow-up says only what changed.
MAX_FOLLOW_UP_WORDS = 4
_FOLLOW_UP_START = re.compile(r"^(?:peki|ya|ayrica|bir de|o zaman|ayni|aynisi)\b")


@dataclass(frozen=True)
class LastCall:
    """A call that ran, with the question it answered (for a reply to a question asked back: both, joined)."""

    tool: str
    arguments: dict
    question: str


class ConversationStore:
    """The last call per user, forgotten ``ttl_seconds`` after it ran (memory.TokenMemory)."""

    def __init__(self, ttl_seconds: float = 600.0, capacity: int = 1000, clock=time.monotonic):
        self._memory = TokenMemory(ttl_seconds, capacity, clock)

    def remember(self, token: str, call: LastCall) -> None:
        """Keeps this call as the context of the next follow-up."""
        self._memory.put(token, call)

    def recall(self, token: str) -> LastCall | None:
        """The last call, left in place for further follow-ups; None when there is none or it is too old."""
        return self._memory.get(token)


def is_follow_up(message: str) -> bool:
    """True for a short message or one that starts like a follow-up ("peki", "ya", "ayrıca", "bir de")."""
    folded = fold_turkish(message).strip()
    if not folded:
        return False
    return len(folded.split()) <= MAX_FOLLOW_UP_WORDS or _FOLLOW_UP_START.match(folded) is not None
