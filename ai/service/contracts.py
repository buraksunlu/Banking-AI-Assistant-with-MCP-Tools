"""Request and response models of the AI service (AI-03).

The service only receives text the backend has already computed and formatted; it never gets raw
amounts, identifiers or a customer id (AI-09, AI-13).
"""

import re
from datetime import date
from decimal import Decimal
from typing import Annotated
from zoneinfo import ZoneInfo, ZoneInfoNotFoundError

from pydantic import AfterValidator, BaseModel, ConfigDict, Field, PlainSerializer

from dates import CustomerDay


def _not_blank(value: str) -> str:
    if not value.strip():
        raise ValueError("Boş olamaz.")
    return value


def _iso_day(value: str) -> str:
    # Exactly YYYY-MM-DD and a real day: date.fromisoformat alone would also accept other ISO forms ("20261010").
    if not re.fullmatch(r"\d{4}-\d{2}-\d{2}", value):
        raise ValueError("YYYY-MM-DD biçiminde geçerli bir gün olmalı.")
    try:
        date.fromisoformat(value)
    except ValueError:
        raise ValueError("YYYY-MM-DD biçiminde geçerli bir gün olmalı.") from None
    return value


def _known_zone(value: str) -> str:
    try:
        ZoneInfo(value)
    except (ZoneInfoNotFoundError, ValueError, OSError):
        raise ValueError("Tanınmayan saat dilimi.") from None
    return value


# Same idea as InputRules.Text on the .NET side: a length range and not only whitespace.
Question = Annotated[str, Field(min_length=1, max_length=500), AfterValidator(_not_blank)]
Label = Annotated[str, Field(min_length=1, max_length=100), AfterValidator(_not_blank)]
FactText = Annotated[str, Field(min_length=1, max_length=200), AfterValidator(_not_blank)]


class _Request(BaseModel):
    # extra="forbid": an unknown field (for example customerId) is rejected, like the API's Disallow rule.
    # strict=True: 123 is not silently turned into "123".
    model_config = ConfigDict(extra="forbid", strict=True)


class Fact(_Request):
    """One line of a backend result, already formatted for people ("Toplam harcama" / "735,00 TL")."""

    label: Label
    text: FactText


class ExplanationRequest(_Request):
    """The user's question and the backend result to explain."""

    question: Question
    facts: list[Fact] = Field(min_length=1, max_length=30)


class ExplanationResponse(BaseModel):
    """The model's explanation and the model that wrote it."""

    answer: str
    model: str


class HealthResponse(BaseModel):
    """Health check result; ``model`` is set by the readiness check only."""

    status: str
    model: str | None = None


class ChatRequest(_Request):
    """One message of the user (AI-14a). The customer comes from the token, never from the body; ``today`` and ``timeZone``
    are the customer's own calendar, sent by the .NET API (CHAT-08 K9-C), never the iPhone."""

    message: Question
    today: Annotated[str, AfterValidator(_iso_day)]
    time_zone: Annotated[str, AfterValidator(_known_zone)] = Field(alias="timeZone")

    def day(self) -> CustomerDay:
        """The customer's day, parsed."""
        return CustomerDay(date.fromisoformat(self.today), self.time_zone)


class ToolCallOut(BaseModel):
    """A tool the turn ran and its arguments."""

    tool: str
    arguments: dict[str, str]


class ChoiceOut(BaseModel):
    """One option of a question back, shown as a tappable row; tapping it sends ``label`` as the next message (CHAT-10 K6-A).
    ``detail`` says what the option is, empty when the label says it alone (P5-A)."""

    label: str
    detail: str


def _json_number(value: Decimal) -> float:
    # Pydantic writes a Decimal as JSON text, and the .NET API reads money as JSON numbers only (JsonNumberHandling.Strict). A
    # backend amount has two decimals, so the float carries it exactly; one that would not survive is refused, never rounded.
    number = float(value)
    if Decimal(repr(number)) != value:
        raise ValueError(f"{value} cannot be written as an exact JSON number.")
    return number


Amount = Annotated[Decimal, PlainSerializer(_json_number, return_type=float, when_used="json")]


class HeadlineOut(BaseModel):
    """The big figure of a card (UX-01 PR4); ``amount`` and ``currency`` are null for a figure that is not money (PR6 K7-B)."""

    amount: Amount | None
    currency: str | None
    text: str


class ChangeOut(BaseModel):
    """The change against the period before; ``percent`` is null when that period had nothing to compare with."""

    percent: Amount | None
    direction: str
    against: str
    against_text: str = Field(serialization_alias="againstText")
    text: str


class BarOut(BaseModel):
    """A ``bars`` row; ``share`` is the bar's length (0-1) and is never shown as text."""

    label: str
    amount: Amount
    text: str
    share: Amount
    direction: str | None


class PointOut(BaseModel):
    """A ``series`` month; ``partial`` marks the running month."""

    label: str
    amount: Amount
    text: str
    share: Amount
    partial: bool


class StatOut(BaseModel):
    """A ``stats`` cell."""

    label: str
    text: str


class EntryOut(BaseModel):
    """A ``list`` line (UX-01 PR6a): a payment or a purchase, with its second line and an optional arrow."""

    label: str
    detail: str
    text: str
    direction: str | None


class BlockOut(BaseModel):
    """A part of a card; the list its ``kind`` names is filled, the other three are empty."""

    kind: str
    rows: list[BarOut]
    points: list[PointOut]
    items: list[StatOut]
    highlight: str | None
    entries: list[EntryOut]


class CardOut(BaseModel):
    """One answer card, built by code from the tool result (UX-01 PR4); every figure is the backend's."""

    title: str
    headline: HeadlineOut
    change: ChangeOut | None
    simple: list[BlockOut]
    detailed: list[BlockOut]
    sentence: str
    # UX-01 PR6 K4-A: code's fixed sentences under the card (an estimate's rule, an investment warning); [] when there are none.
    notes: list[str]


class ChatResponse(BaseModel):
    """The reply of one chat turn; ``toolCalls`` and ``facts`` say what the answer is based on (AI-11, CHAT-07),
    ``suggestions`` are the questions to offer under it (CHAT-06), ``choices`` a question's options (CHAT-10 K4-A)."""

    type: str
    answer: str
    tool_calls: list[ToolCallOut] = Field(serialization_alias="toolCalls")
    facts: list[Fact]
    suggestions: list[str]
    choices: list[ChoiceOut]
    # PR3b D6-A: a row inviting an answer of the user's own, which opens the keyboard; "" when the options are all there is.
    input_hint: str = Field(serialization_alias="inputHint")
    # UX-01 PR4: the answer's cards, one per currency; empty for a tool without cards and for every reply that is not an answer.
    cards: list[CardOut]
    model: str


class SuggestionsResponse(BaseModel):
    """The questions to show when the chat opens (CHAT-06)."""

    suggestions: list[str]
