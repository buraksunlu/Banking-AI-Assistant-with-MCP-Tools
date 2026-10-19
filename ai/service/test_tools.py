import json
import socket
import threading
import time
from decimal import Decimal
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlsplit

import pytest

from tools import (
    PERIODS,
    ApiSettings,
    BackendTimeout,
    BackendUnavailable,
    RestToolGateway,
    SessionInvalid,
    ToolCallRejected,
    tool_definitions,
    validate_arguments,
)

SUMMARY = {"period": {"kind": "Yesterday", "startDate": "2026-10-09", "endDate": "2026-10-09"},
           "currencies": [{"currencyCode": "TRY", "totalAmount": 145.00, "transactionCount": 1,
                           "uncategorizedAmount": 145.00, "uncategorizedCount": 1}]}


class FakeApi:
    """Answers like the .NET API with a scripted status and body, and records what it was asked."""

    def __init__(self, status: int = 200, body: object = SUMMARY, delay: float = 0.0):
        self.status, self.body, self.delay = status, body, delay
        self.requests: list[tuple[str, dict, str | None]] = []
        fake = self

        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                parts = urlsplit(self.path)
                fake.requests.append((parts.path, parse_qs(parts.query), self.headers.get("Authorization")))
                time.sleep(fake.delay)
                # The API writes money as JSON numbers with two decimals ("145.00"); keep that text exactly.
                data = json.dumps(fake.body).replace("145.0,", "145.00,").replace("145.0}", "145.00}").encode()
                try:
                    self.send_response(fake.status)
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
        self.url = f"http://127.0.0.1:{self.server.server_address[1]}"

    def __enter__(self):
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        return self

    def __exit__(self, *exc):
        self.server.shutdown()
        self.server.server_close()


def gateway(url: str) -> RestToolGateway:
    return RestToolGateway(ApiSettings(base_url=url))


def soon(seconds: float = 5.0) -> float:
    return time.monotonic() + seconds


# Tool definitions and argument checks


def test_three_tools_with_every_period_name():
    definitions = tool_definitions()

    assert [d["function"]["name"] for d in definitions] == [
        "get_spending_summary", "get_category_breakdown", "get_merchant_spending"]
    assert PERIODS == ("Today", "Yesterday", "ThisWeek", "LastWeek", "Last7Days", "Last30Days",
                       "ThisMonth", "LastMonth", "Last3Months", "Last6Months", "ThisYear")
    for definition in definitions:
        parameters = definition["function"]["parameters"]
        assert parameters["properties"]["period"]["enum"] == list(PERIODS)
        assert "Yesterday=dün" in definition["function"]["description"]
    assert definitions[2]["function"]["parameters"]["required"] == ["merchant", "period"]


@pytest.mark.parametrize(
    ("tool", "arguments", "valid"),
    [
        ("get_spending_summary", {"period": "Yesterday"}, True),
        ("get_merchant_spending", {"merchant": "Kahve Durağı", "period": "Last7Days"}, True),
        ("get_spending_summary", {"period": "yesterday"}, False),          # exact names only, like the API
        ("get_spending_summary", {"period": "Tomorrow"}, False),
        ("get_spending_summary", {"period": "Custom"}, False),             # dates are never the model's to choose
        ("get_spending_summary", {}, False),
        ("get_spending_summary", {"period": "ThisMonth", "accountId": "x"}, False),
        ("get_merchant_spending", {"merchant": "  ", "period": "ThisMonth"}, False),
        ("get_merchant_spending", {"merchant": "x" * 151, "period": "ThisMonth"}, False),
        ("get_merchant_spending", {"period": "ThisMonth"}, False),
        ("transfer_money", {"amount": 500}, False),
    ],
)
def test_arguments_are_checked_before_the_api_is_called(tool, arguments, valid):
    assert (validate_arguments(tool, arguments) == arguments) is valid
    if not valid:
        assert validate_arguments(tool, arguments) is None


# The bridge


def test_summary_goes_to_its_endpoint_with_the_users_token():
    with FakeApi() as api:
        body = gateway(api.url).call("get_spending_summary", {"period": "Yesterday"}, "token-123", soon())

    assert api.requests == [("/api/analytics/spending-summary", {"period": ["Yesterday"]}, "Bearer token-123")]
    # Money is read as Decimal, never float: 145.00 stays exactly 145.00.
    assert body["currencies"][0]["totalAmount"] == Decimal("145.00")
    assert isinstance(body["currencies"][0]["totalAmount"], Decimal)


def test_category_and_merchant_endpoints_and_turkish_names():
    with FakeApi() as api:
        gateway(api.url).call("get_category_breakdown", {"period": "ThisWeek"}, "t", soon())
        gateway(api.url).call("get_merchant_spending", {"merchant": "Kahve Durağı", "period": "Last7Days"}, "t", soon())

    assert [(path, query) for path, query, _ in api.requests] == [
        ("/api/analytics/category-breakdown", {"period": ["ThisWeek"]}),
        ("/api/analytics/merchant-spending", {"merchant": ["Kahve Durağı"], "period": ["Last7Days"]}),
    ]


@pytest.mark.parametrize(
    ("status", "error"),
    [(401, SessionInvalid), (400, ToolCallRejected), (404, ToolCallRejected), (500, BackendUnavailable),
     (503, BackendUnavailable)],
)
def test_api_errors_become_bridge_errors(status, error):
    problem = {"title": "x", "status": status, "code": "x", "traceId": "0"}
    with FakeApi(status, problem) as api:
        with pytest.raises(error):
            gateway(api.url).call("get_spending_summary", {"period": "Today"}, "t", soon())


def test_closed_port_is_unavailable():
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        url = f"http://127.0.0.1:{s.getsockname()[1]}"
    with pytest.raises(BackendUnavailable):
        gateway(url).call("get_spending_summary", {"period": "Today"}, "t", soon())


def test_slow_api_runs_into_the_chat_deadline():
    with FakeApi(delay=2.0) as api:
        started = time.monotonic()
        with pytest.raises(BackendTimeout):
            gateway(api.url).call("get_spending_summary", {"period": "Today"}, "t", soon(0.5))
    assert time.monotonic() - started < 1.5


def test_no_request_when_the_deadline_has_passed():
    with FakeApi() as api:
        with pytest.raises(BackendTimeout):
            gateway(api.url).call("get_spending_summary", {"period": "Today"}, "t", time.monotonic() - 1)
    assert api.requests == []


@pytest.mark.parametrize(
    ("env", "url"),
    [({}, "http://127.0.0.1:5248"), ({"BANKINGAI_API_URL": ""}, "http://127.0.0.1:5248"),
     ({"BANKINGAI_API_URL": "http://api:8080/"}, "http://api:8080")],
)
def test_api_address_setting(env, url):
    assert ApiSettings.from_env(env).base_url == url
