"""The chat's tools on the .NET MCP server (MCP-b): listing them for the model and running the one it chose.

Every call carries the user's own token in its header (docs/security.md); the MCP server takes the customer from it,
checks the arguments against the schema it published and computes with the same services as the API. The token never
goes into a request body, a model or a log.
"""

import json
import time
import urllib.error
import urllib.request
from collections.abc import Awaitable, Callable, Mapping
from dataclasses import dataclass
from decimal import Decimal
from typing import TypeVar
from urllib.parse import urlsplit

import anyio
import httpx2
from mcp import Client, MCPError
from mcp.client.streamable_http import streamable_http_client

from tools import BackendTimeout, BackendUnavailable, SessionInvalid, ToolArgumentsRejected, ToolCallRejected

DEFAULT_MCP_URL = "http://127.0.0.1:5249/mcp"

# Pinned rather than probed: the server speaks this revision, and pinning saves a discovery request on every call.
PROTOCOL_VERSION = "2026-07-28"

# The server's own readiness answer is a single database check; more than this means it cannot serve a chat either.
READY_TIMEOUT_SECONDS = 3.0

# INV-03 PR1 bridge (removed in PR2): the monthly saving's months until the chat asks the user for them.
_MONTHLY_SAVING = "what_if_monthly"
_BRIDGE_MONTHS = 12

T = TypeVar("T")


@dataclass(frozen=True)
class McpSettings:
    """Where the MCP server is."""

    url: str

    @classmethod
    def from_env(cls, env: Mapping[str, str]) -> "McpSettings":
        """Reads BANKINGAI_MCP_URL; empty means the local default, a wrong address stops startup (K5-C)."""
        value = (env.get("BANKINGAI_MCP_URL") or "").strip()
        if not value:
            return cls(DEFAULT_MCP_URL)
        parts = urlsplit(value)
        if parts.scheme not in ("http", "https") or not parts.netloc:
            raise ValueError("BANKINGAI_MCP_URL must be an absolute http or https address.")
        return cls(value)

    @property
    def health_url(self) -> str:
        """The server's open health check, next to the MCP endpoint (K6-C)."""
        parts = urlsplit(self.url)
        return f"{parts.scheme}://{parts.netloc}/health"


class _Unauthorized(Exception):
    """The server answered 401; the SDK would bury it in a generic error, so the response hook raises this instead."""


class _ServerFailed(Exception):
    """The server answered 5xx; buried by the SDK the same way, and it means "unavailable", not "bad request"."""


class McpToolGateway:
    """Lists and runs tools on the MCP server for the user whose token is given."""

    def __init__(self, settings: McpSettings):
        self.settings = settings
        # Replaceable in tests, so time limits can be checked without waiting.
        self.clock = time.monotonic

    def list_tools(self, token: str, deadline: float) -> list[dict]:
        """The published tools in the shape the model's tool calling takes (name, description, JSON schema)."""
        listed = self._run(token, deadline, lambda client: client.list_tools(), protocol_error=BackendUnavailable)
        return [{"type": "function", "function": {"name": tool.name, "description": tool.description or "",
                                                  "parameters": tool.input_schema}}
                for tool in listed.tools]

    def call(self, tool: str, arguments: dict, token: str, deadline: float) -> dict:
        """The tool's result, the API's own JSON; failures surface as the bridge errors of tools.py."""
        if tool == _MONTHLY_SAVING and "months" not in arguments:
            # INV-03 PR1 bridge: the server requires the months now, and the chat asks for them only from PR2 on; until then it
            # saves for twelve, as before. PR2 removes this.
            arguments = {**arguments, "months": _BRIDGE_MONTHS}
        result = self._run(token, deadline, lambda client: client.call_tool(tool, arguments), protocol_error=ToolCallRejected)
        text = next((block.text for block in result.content if getattr(block, "type", None) == "text"), None)
        if result.is_error:
            raise _tool_error(text)
        try:
            # The text, not the structured copy: the SDK parses that one into floats (145.00 → 145.0), and money must
            # never pass through float.
            answer = json.loads(text, parse_float=Decimal) if text is not None else None
        except json.JSONDecodeError:
            answer = None
        if not isinstance(answer, dict):
            raise BackendUnavailable("The MCP server's result is not the tool's JSON object.")
        return answer

    def is_ready(self) -> bool:
        """Whether the MCP server and its database answer; never raises."""
        try:
            with urllib.request.urlopen(self.settings.health_url, timeout=READY_TIMEOUT_SECONDS) as response:
                return response.status == 200
        except (urllib.error.URLError, OSError, TimeoutError):
            return False

    def _run(self, token: str, deadline: float, action: Callable[[Client], Awaitable[T]], protocol_error: type[Exception]) -> T:
        # The chat is synchronous and runs on a worker thread without an event loop, so each call gets its own short
        # loop (K2-A). The whole call, connection included, takes from the turn's single deadline.
        remaining = deadline - self.clock()
        if remaining <= 0:
            raise BackendTimeout("No time left for the MCP call.")
        try:
            return anyio.run(self._with_client, token, remaining, action)
        except BaseException as error:  # the SDK raises exception groups; the cause is inside
            raise _translate(_cause(error), remaining, self.settings.url, protocol_error) from None

    async def _with_client(self, token: str, remaining: float, action: Callable[[Client], Awaitable[T]]) -> T:
        async def refuse_failures(response: httpx2.Response) -> None:
            if response.status_code == 401:
                raise _Unauthorized()
            if response.status_code >= 500:
                raise _ServerFailed(f"HTTP {response.status_code}")

        headers = {"Authorization": f"Bearer {token}"}
        async with httpx2.AsyncClient(headers=headers, timeout=remaining, event_hooks={"response": [refuse_failures]}) as http:
            with anyio.fail_after(remaining):
                async with Client(streamable_http_client(self.settings.url, http_client=http), mode=PROTOCOL_VERSION) as client:
                    return await action(client)


def _cause(error: BaseException) -> BaseException:
    # Exception groups nest; the first leaf is what actually went wrong.
    while isinstance(error, BaseExceptionGroup) and error.exceptions:
        error = error.exceptions[0]
    return error


def _translate(cause: BaseException, remaining: float, url: str, protocol_error: type[Exception]) -> BaseException:
    if isinstance(cause, _Unauthorized):
        return SessionInvalid("The MCP server refused the token.")
    if isinstance(cause, (TimeoutError, httpx2.TimeoutException)):
        return BackendTimeout(f"The MCP server did not answer within {remaining:.1f} s.")
    if isinstance(cause, (httpx2.TransportError, _ServerFailed)):
        return BackendUnavailable(f"Cannot reach the MCP server at {url}: {cause}")
    if isinstance(cause, MCPError):
        # A protocol error: an unknown tool on a call, or a server that cannot list (502 vs 503 for the caller).
        return protocol_error(f"The MCP server refused the request: {cause}")
    if isinstance(cause, (KeyboardInterrupt, SystemExit)):
        return cause
    return BackendUnavailable(f"The MCP call failed: {type(cause).__name__}: {cause}")


def _tool_error(text: str | None) -> Exception:
    """The server's {code, message} tool error (MCP-a K10-A) as a bridge error; anything else is a server fault."""
    try:
        error = json.loads(text or "")
        code, message = error["code"], error["message"]
    except (json.JSONDecodeError, KeyError, TypeError):
        return BackendUnavailable("The MCP server reported an error outside the contract.")
    if code == "invalid_arguments":
        return ToolArgumentsRejected(message)
    if code == "session_invalid":
        return SessionInvalid(message)
    return BackendUnavailable(f"The MCP server failed ({code}).")
