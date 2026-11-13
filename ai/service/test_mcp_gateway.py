"""MCP-b: the AI service's tools come from the .NET MCP server (MCP-12, MCP-13).

A small JSON-RPC server stands in for the .NET MCP server and answers exactly as it does under the pinned 2026-07-28
protocol (raw exchanges recorded from the real server on 20 Oct 2026): every result is "complete" and names the
server, listings say they must not be cached, tool results carry the API's JSON as text and as structured content, tool
errors are {"code", "message"} with isError. Tests against the real server run only when it is up with the demo moment.
"""

import json
import os
import socket
import threading
import time
import urllib.request
from decimal import Decimal
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import pytest

from mcp_gateway import DEFAULT_MCP_URL, McpSettings, McpToolGateway
from tools import BackendTimeout, BackendUnavailable, SessionInvalid, ToolArgumentsRejected, ToolCallRejected

TOKEN = "secret-token-123"
PERIODS = ["Today", "Yesterday", "ThisWeek", "LastWeek", "Last7Days", "Last30Days",
           "ThisMonth", "LastMonth", "Last3Months", "Last6Months", "ThisYear"]
SUMMARY_TOOL = {
    "name": "get_spending_summary",
    "description": "Bir dönemdeki toplam harcamayı para birimi başına döndürür.",
    "inputSchema": {"type": "object", "properties": {"period": {"type": "string", "enum": PERIODS}}, "required": ["period"]},
    "annotations": {"readOnlyHint": True, "destructiveHint": False},
}
# The API writes money as JSON numbers with two decimals; the text keeps them, the structured copy does not survive
# the SDK's parsing (145.00 becomes the float 145.0).
SUMMARY_TEXT = ('{"period":{"kind":"Yesterday","startDate":"2026-10-09","endDate":"2026-10-09"},"currencies":[{"currencyCode":'
                '"TRY","totalAmount":145.00,"transactionCount":1,"uncategorizedAmount":145.00,"uncategorizedCount":1}]}')


SERVER_META = {"io.modelcontextprotocol/serverInfo": {"name": "BankingAI", "version": "1.0"}}


def complete(answer: dict) -> dict:
    """The fields every 2026-07-28 result carries; the SDK refuses a result without them."""
    return {**answer, "resultType": "complete", "_meta": SERVER_META}


def result(text: str) -> dict:
    return complete({"content": [{"type": "text", "text": text}], "structuredContent": json.loads(text)})


def tool_error(code: str, message: str = "Açıklama.") -> dict:
    return complete({"content": [{"type": "text", "text": json.dumps({"code": code, "message": message})}], "isError": True})


class FakeMcp:
    """Answers tools/list and tools/call like the .NET MCP server, and records every request it gets."""

    def __init__(self, call: dict | None = None, status: int = 200, delay: float = 0.0, health: int = 200,
                 rpc_error: dict | None = None):
        self.call, self.status, self.delay, self.health, self.rpc_error = call or result(SUMMARY_TEXT), status, delay, health, rpc_error
        self.requests: list[dict] = []
        fake = self

        class Handler(BaseHTTPRequestHandler):
            def do_POST(self):
                body = self.rfile.read(int(self.headers.get("Content-Length", 0))).decode()
                rpc = json.loads(body)
                fake.requests.append({"path": self.path, "method": rpc.get("method"), "params": rpc.get("params"),
                                      "headers": dict(self.headers.items()), "body": body})
                time.sleep(fake.delay)
                if fake.status != 200:
                    return self._send(fake.status, b"")
                if fake.rpc_error is not None:
                    answer = {"jsonrpc": "2.0", "id": rpc["id"], "error": fake.rpc_error}
                elif rpc["method"] == "tools/list":
                    listing = complete({"tools": [SUMMARY_TOOL], "ttlMs": 0, "cacheScope": "private"})
                    answer = {"jsonrpc": "2.0", "id": rpc["id"], "result": listing}
                else:
                    answer = {"jsonrpc": "2.0", "id": rpc["id"], "result": fake.call}
                self._send(200, json.dumps(answer).encode())

            def do_GET(self):
                if self.path == "/health":
                    time.sleep(fake.delay)
                    return self._send(fake.health, b'{"status":"ok","database":true}')
                self._send(405, b"")

            def do_DELETE(self):
                self._send(405, b"")

            def _send(self, status: int, data: bytes):
                try:
                    self.send_response(status)
                    self.send_header("Content-Type", "application/json")
                    self.send_header("Content-Length", str(len(data)))
                    self.end_headers()
                    self.wfile.write(data)
                except (BrokenPipeError, ConnectionResetError):
                    pass

            def log_message(self, *args):
                pass

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.server.daemon_threads = True
        self.url = f"http://127.0.0.1:{self.server.server_address[1]}/mcp"

    def __enter__(self):
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        return self

    def __exit__(self, *exc):
        self.server.shutdown()
        self.server.server_close()


def gateway(url: str) -> McpToolGateway:
    return McpToolGateway(McpSettings(url=url))


def soon(seconds: float = 5.0) -> float:
    return time.monotonic() + seconds


def closed_port_url() -> str:
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        return f"http://127.0.0.1:{probe.getsockname()[1]}/mcp"


# Listing


def test_tools_are_listed_in_the_shape_the_model_takes():
    with FakeMcp() as mcp:
        definitions = gateway(mcp.url).list_tools(TOKEN, soon())

    assert definitions == [{"type": "function", "function": {
        "name": "get_spending_summary", "description": SUMMARY_TOOL["description"], "parameters": SUMMARY_TOOL["inputSchema"]}}]


def test_the_token_travels_in_the_header_only():
    with FakeMcp() as mcp:
        gateway(mcp.url).list_tools(TOKEN, soon())
        gateway(mcp.url).call("get_spending_summary", {"period": "Yesterday"}, TOKEN, soon())

    # The SDK may list once more around a call on its own; what matters is that every request carries the token.
    assert {r["method"] for r in mcp.requests} == {"tools/list", "tools/call"}
    call = next(r for r in mcp.requests if r["method"] == "tools/call")
    for request in mcp.requests:
        assert request["headers"]["Authorization"] == f"Bearer {TOKEN}"
        assert TOKEN not in request["body"]
        # The protocol version is pinned, so no discovery or handshake request precedes the call.
        assert request["headers"]["mcp-protocol-version"] == "2026-07-28"
    assert (call["params"]["name"], call["params"]["arguments"]) == ("get_spending_summary", {"period": "Yesterday"})


# Calling


@pytest.mark.parametrize(("arguments", "sent"), [
    ({"amount": 5000, "currencyCode": "TRY"}, {"amount": 5000, "currencyCode": "TRY", "months": 12}),
    ({"fromAdvice": True}, {"fromAdvice": True, "months": 12}),
    ({"fromAdvice": True, "months": 6}, {"fromAdvice": True, "months": 6}),
])
def test_the_monthly_saving_keeps_its_twelve_months_until_the_chat_asks(arguments, sent):
    # INV-03 PR1: the server requires the months now; the chat still saves for twelve until PR2 asks the user.
    with FakeMcp() as mcp:
        gateway(mcp.url).call("what_if_monthly", arguments, TOKEN, soon())

    call = next(r for r in mcp.requests if r["method"] == "tools/call")
    assert call["params"]["arguments"] == sent


def test_money_comes_back_as_decimal_with_its_two_decimals():
    with FakeMcp() as mcp:
        answer = gateway(mcp.url).call("get_spending_summary", {"period": "Yesterday"}, TOKEN, soon())

    total = answer["currencies"][0]["totalAmount"]
    # Never a float: formatting refuses floats, and 0.1 + 0.2 is not 0.3 there.
    assert isinstance(total, Decimal) and str(total) == "145.00"
    assert answer["period"]["kind"] == "Yesterday"


@pytest.mark.parametrize(
    ("call", "error"),
    [
        (tool_error("invalid_arguments", "period şunlardan biri olmalı"), ToolArgumentsRejected),
        (tool_error("session_invalid"), SessionInvalid),
        (tool_error("internal_error"), BackendUnavailable),
        (tool_error("something_new"), BackendUnavailable),
        (complete({"content": [{"type": "text", "text": "not json"}], "isError": True}), BackendUnavailable),
        (complete({"content": [{"type": "text", "text": "not json"}]}), BackendUnavailable),
        (complete({"content": [{"type": "text", "text": "[1, 2]"}]}), BackendUnavailable),
        (complete({"content": []}), BackendUnavailable),
    ],
)
def test_tool_errors_and_broken_results_become_bridge_errors(call, error):
    with FakeMcp(call=call) as mcp, pytest.raises(error):
        gateway(mcp.url).call("get_spending_summary", {"period": "Yesterday"}, TOKEN, soon())


@pytest.mark.parametrize("action", ["list", "call"])
def test_a_refused_token_is_an_invalid_session(action):
    with FakeMcp(status=401) as mcp, pytest.raises(SessionInvalid):
        run(gateway(mcp.url), action)


@pytest.mark.parametrize("action", ["list", "call"])
def test_a_server_that_is_not_running_is_unavailable(action):
    with pytest.raises(BackendUnavailable):
        run(gateway(closed_port_url()), action)


@pytest.mark.parametrize("action", ["list", "call"])
def test_a_server_failure_is_unavailable(action):
    with FakeMcp(status=500) as mcp, pytest.raises(BackendUnavailable):
        run(gateway(mcp.url), action)


def test_a_protocol_error_on_a_call_is_a_rejected_tool_call():
    with FakeMcp(rpc_error={"code": -32602, "message": "Unknown tool: 'create_transaction'"}) as mcp:
        with pytest.raises(ToolCallRejected):
            gateway(mcp.url).call("create_transaction", {}, TOKEN, soon())


@pytest.mark.parametrize("action", ["list", "call"])
def test_a_slow_server_runs_into_the_chat_deadline(action):
    with FakeMcp(delay=3.0) as mcp:
        started = time.monotonic()
        with pytest.raises(BackendTimeout):
            run(gateway(mcp.url), action, deadline=soon(0.5))
        assert time.monotonic() - started < 2.0


@pytest.mark.parametrize("action", ["list", "call"])
def test_no_request_when_the_deadline_has_passed(action):
    with FakeMcp() as mcp:
        with pytest.raises(BackendTimeout):
            run(gateway(mcp.url), action, deadline=time.monotonic() - 1)
        assert mcp.requests == []


def run(target: McpToolGateway, action: str, deadline: float | None = None):
    deadline = soon() if deadline is None else deadline
    if action == "list":
        return target.list_tools(TOKEN, deadline)
    return target.call("get_spending_summary", {"period": "Yesterday"}, TOKEN, deadline)


# Readiness (K6-C)


@pytest.mark.parametrize(("health", "ready"), [(200, True), (503, False)])
def test_ready_follows_the_servers_health(health, ready):
    with FakeMcp(health=health) as mcp:
        assert gateway(mcp.url).is_ready() is ready


def test_not_ready_when_the_server_is_not_running():
    assert gateway(closed_port_url()).is_ready() is False


def test_not_ready_when_the_health_check_is_slow():
    with FakeMcp(delay=5.0) as mcp:
        started = time.monotonic()
        assert gateway(mcp.url).is_ready() is False
        assert time.monotonic() - started < 4.5


# Settings (K5-C)


@pytest.mark.parametrize(
    ("env", "url", "health"),
    [
        ({}, DEFAULT_MCP_URL, "http://127.0.0.1:5249/health"),
        ({"BANKINGAI_MCP_URL": ""}, DEFAULT_MCP_URL, "http://127.0.0.1:5249/health"),
        ({"BANKINGAI_MCP_URL": " http://mcp.test:9000/mcp "}, "http://mcp.test:9000/mcp", "http://mcp.test:9000/health"),
    ],
)
def test_mcp_address_setting(env, url, health):
    settings = McpSettings.from_env(env)
    assert (settings.url, settings.health_url) == (url, health)


@pytest.mark.parametrize("value", ["127.0.0.1:5249/mcp", "ftp://127.0.0.1:5249/mcp", "not an address", "/mcp"])
def test_a_wrong_mcp_address_stops_startup(value):
    with pytest.raises(ValueError, match="BANKINGAI_MCP_URL"):
        McpSettings.from_env({"BANKINGAI_MCP_URL": value})


# The real .NET MCP server (K4-D): only when it is running with the demo moment.


def _real_token() -> str | None:
    key = os.environ.get("BANKINGAI_JWT_SIGNING_KEY")
    if not key:
        return None
    import jwt  # PyJWT comes with the mcp package; used here only to sign a test token like TestTokens does in .NET.

    now = int(time.time())
    return jwt.encode({"sub": "10000000-0000-0000-0000-000000000001", "iss": "BankingAI.Api", "aud": "BankingAI",
                       "iat": now, "nbf": now, "exp": now + 600}, key, algorithm="HS256")


def _real_server_up() -> bool:
    try:
        with urllib.request.urlopen(McpSettings.from_env(os.environ).health_url, timeout=1) as response:
            return response.status == 200 and _real_token() is not None
    except Exception:
        return False


real = pytest.mark.skipif(not _real_server_up(), reason="the .NET MCP server is not running (or no signing key)")


@real
def test_real_server_lists_the_published_tools():
    names = {d["function"]["name"] for d in McpToolGateway(McpSettings.from_env(os.environ)).list_tools(_real_token(), soon())}
    assert names == {"get_spending_summary", "get_category_breakdown", "get_merchant_spending",  # MCP-a
                     "get_merchant_breakdown", "get_highest_transactions", "compare_spending", "get_monthly_trend",  # MCP-08
                     "get_recurring_payments", "get_upcoming_payments",
                     "check_budget", "simulate_savings"}  # MCP-17/18


@real
def test_real_server_computes_yesterday_and_refuses_a_lowercase_period():
    target = McpToolGateway(McpSettings.from_env(os.environ))
    answer = target.call("get_spending_summary", {"period": "Yesterday"}, _real_token(), soon())
    if answer["period"]["endDate"] != "2026-10-09":
        pytest.skip("the MCP server is not running with BANKINGAI_DEMO_NOW=2026-10-10T12:00:00Z")
    assert str(answer["currencies"][0]["totalAmount"]) == "145.00"
    with pytest.raises(ToolArgumentsRejected):
        target.call("get_spending_summary", {"period": "yesterday"}, _real_token(), soon())
