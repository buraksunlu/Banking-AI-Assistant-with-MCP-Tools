"""The tools the model may choose and the temporary bridge that runs them on the .NET API (AI-14a, K2-A).

Until MCP (MCP-12) the bridge calls the existing analytics endpoints with the user's own token: the API takes the
customer from the token and checks every argument again, so the model never names a customer.
"""

import json
import time
from collections.abc import Mapping
from dataclasses import dataclass
from decimal import Decimal
from typing import Annotated, Literal, Protocol

import httpx
from pydantic import AfterValidator, BaseModel, ConfigDict, Field, ValidationError

# Every named period of the API except Custom: dates are never the model's to choose (ANA-20).
PERIODS: tuple[str, ...] = ("Today", "Yesterday", "ThisWeek", "LastWeek", "Last7Days", "Last30Days",
                            "ThisMonth", "LastMonth", "Last3Months", "Last6Months", "ThisYear")

PERIOD_HELP = ("period: Today=bugün, Yesterday=dün, ThisWeek=bu hafta (pazartesiden bugüne), LastWeek=geçen hafta, "
               "Last7Days=son 7 gün, Last30Days=son 30 gün, ThisMonth=bu ay, LastMonth=geçen ay, Last3Months=son 3 ay, "
               "Last6Months=son 6 ay, ThisYear=bu yıl.")

DEFAULT_API_URL = "http://127.0.0.1:5248"

Period = Literal[PERIODS]


def _not_blank(value: str) -> str:
    if not value.strip():
        raise ValueError("blank")
    return value


class _Arguments(BaseModel):
    # strict + forbid: the arguments must be exactly what the tool takes; "yesterday" or an extra accountId is refused.
    model_config = ConfigDict(extra="forbid", strict=True)


class PeriodArguments(_Arguments):
    period: Period


class MerchantArguments(_Arguments):
    # 150 is the API's limit for a merchant name (Transactions.NormalizedMerchant).
    merchant: Annotated[str, Field(min_length=1, max_length=150), AfterValidator(_not_blank)]
    period: Period


@dataclass(frozen=True)
class _Tool:
    description: str
    arguments: type[_Arguments]
    path: str


_TOOLS = {
    "get_spending_summary": _Tool(
        "Bir dönemdeki toplam harcamayı para birimi başına döndürür.", PeriodArguments,
        "/api/analytics/spending-summary"),
    "get_category_breakdown": _Tool(
        "Bir dönemdeki harcamanın kategorilere dağılımını döndürür.", PeriodArguments,
        "/api/analytics/category-breakdown"),
    "get_merchant_spending": _Tool(
        "Bir dönemde tek bir işyerine (merchant) yapılan harcamayı döndürür. merchant: kullanıcının söylediği işyeri adı.",
        MerchantArguments, "/api/analytics/merchant-spending"),
}


class BackendUnavailable(Exception):
    """The .NET API cannot be reached or failed on its side."""


class SessionInvalid(Exception):
    """The .NET API refused the user's token (expired, or the customer no longer exists)."""


class ToolCallRejected(Exception):
    """The .NET API refused the arguments the model produced."""


class BackendTimeout(Exception):
    """The .NET API did not answer within the time left for the chat."""


@dataclass(frozen=True)
class ApiSettings:
    """Where the .NET API is."""

    base_url: str

    @classmethod
    def from_env(cls, env: Mapping[str, str]) -> "ApiSettings":
        """Reads BANKINGAI_API_URL (default http://127.0.0.1:5248)."""
        return cls((env.get("BANKINGAI_API_URL") or DEFAULT_API_URL).rstrip("/"))


def tool_definitions() -> list[dict]:
    """The tools in the shape the model's tool calling expects (name, description, JSON schema of the arguments)."""
    # The period meanings go into the description: the AI-14 probe chose the right one in 12 of 12 questions this way.
    return [
        {"type": "function", "function": {"name": name, "description": f"{tool.description} {PERIOD_HELP}",
                                          "parameters": tool.arguments.model_json_schema()}}
        for name, tool in _TOOLS.items()
    ]


def validate_arguments(tool: str, arguments: dict) -> dict | None:
    """The arguments if the tool exists and they follow its schema exactly; otherwise None."""
    spec = _TOOLS.get(tool)
    if spec is None:
        return None
    try:
        return spec.arguments.model_validate(arguments).model_dump()
    except ValidationError:
        return None


class ToolGateway(Protocol):
    """Runs a tool for the user whose token is given."""

    def call(self, tool: str, arguments: dict, token: str, deadline: float) -> dict: ...


class RestToolGateway:
    """Runs tools as GET requests on the .NET API; JSON numbers come back as Decimal."""

    def __init__(self, settings: ApiSettings):
        self.settings = settings
        # Replaceable in tests, so time limits can be checked without waiting.
        self.clock = time.monotonic

    def call(self, tool: str, arguments: dict, token: str, deadline: float) -> dict:
        """The endpoint's JSON body; failures surface as the four bridge errors above."""
        remaining = deadline - self.clock()
        if remaining <= 0:
            raise BackendTimeout("No time left for the API call.")

        url = f"{self.settings.base_url}{_TOOLS[tool].path}"
        try:
            with httpx.Client(timeout=remaining) as client:
                response = client.get(url, params=arguments, headers={"Authorization": f"Bearer {token}"})
        except httpx.TimeoutException:
            raise BackendTimeout(f"The API did not answer within {remaining:.1f} s.") from None
        except httpx.TransportError as error:
            raise BackendUnavailable(f"Cannot reach the API at {self.settings.base_url}: {error}") from None

        if response.status_code == 401:
            raise SessionInvalid("The API refused the token.")
        if response.status_code >= 500:
            raise BackendUnavailable(f"The API failed ({response.status_code}).")
        if response.status_code >= 400:
            # The model's arguments passed our check but not the API's; the API's own message names the field.
            raise ToolCallRejected(f"The API rejected {tool} ({response.status_code}): {response.text[:200]}")
        try:
            # parse_float=Decimal: money must never pass through float (0.1 + 0.2 is not 0.3 there).
            return json.loads(response.text, parse_float=Decimal)
        except ValueError:
            raise BackendUnavailable("The API answer is not JSON.") from None
