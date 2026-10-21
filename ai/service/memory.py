"""Short-lived memory per user, for the chat's waiting question (CHAT-03) and last call (CHAT-02).

Kept under a digest of the user's token, never the token or the customer id: the AI service does not verify tokens, so only
the very same token finds what was kept. Bounded and thread-safe (FastAPI runs the chat on worker threads); nothing is
written to disk, so a restart forgets everything, which only means the user asks again.
"""

import hashlib
import threading
import time
from collections import OrderedDict


class TokenMemory:
    """One value per token, forgotten ``ttl_seconds`` after it was stored; the oldest entry goes when ``capacity`` is passed."""

    def __init__(self, ttl_seconds: float, capacity: int, clock=time.monotonic):
        self._ttl, self._capacity, self._clock = ttl_seconds, capacity, clock
        self._items: OrderedDict[str, tuple[float, object]] = OrderedDict()
        self._lock = threading.Lock()

    def put(self, token: str, value) -> None:
        """Stores the value for this token, replacing an older one and restarting its time."""
        key = _key(token)
        with self._lock:
            self._items.pop(key, None)
            self._items[key] = (self._clock(), value)
            while len(self._items) > self._capacity:
                self._items.popitem(last=False)

    def get(self, token: str):
        """The value, left in place; None when there is none or it is too old."""
        with self._lock:
            item = self._items.get(_key(token))
        return self._fresh(item)

    def pop(self, token: str):
        """The value, removed; None when there is none or it is too old."""
        with self._lock:
            item = self._items.pop(_key(token), None)
        return self._fresh(item)

    def _fresh(self, item):
        if item is None or self._clock() - item[0] > self._ttl:
            return None
        return item[1]


def _key(token: str) -> str:
    return hashlib.sha256(token.encode("utf-8")).hexdigest()
