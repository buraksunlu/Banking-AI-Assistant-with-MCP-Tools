"""Request and response models of the AI service (AI-03).

The service only receives text the backend has already computed and formatted; it never gets raw
amounts, identifiers or a customer id (AI-09, AI-13).
"""

from typing import Annotated

from pydantic import AfterValidator, BaseModel, ConfigDict, Field


def _not_blank(value: str) -> str:
    if not value.strip():
        raise ValueError("Boş olamaz.")
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
    """One message of the user (AI-14a). The customer comes from the token, never from the body."""

    message: Question


class ToolCallOut(BaseModel):
    """A tool the turn ran and its arguments."""

    tool: str
    arguments: dict[str, str]


class ChatResponse(BaseModel):
    """The reply of one chat turn; ``toolCalls`` and ``facts`` say what the answer is based on (AI-11, CHAT-07)."""

    type: str
    answer: str
    tool_calls: list[ToolCallOut] = Field(serialization_alias="toolCalls")
    facts: list[Fact]
    model: str
