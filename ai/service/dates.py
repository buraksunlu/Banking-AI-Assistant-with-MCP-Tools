"""The customer's own calendar, as the .NET API sends it with every chat message (CHAT-08 K9-C).

"Today" and the time zone come from the API (its business clock and the customer's record), never from this service's own
clock: a date read from the user's words ("geçen salı") must mean what the analyses mean by it, and the demo moment is
fixed on the .NET side only. Standard library only.
"""

from dataclasses import dataclass
from datetime import date


@dataclass(frozen=True)
class CustomerDay:
    """The customer's today and the id of their time zone ("Europe/Istanbul")."""

    today: date
    time_zone: str
