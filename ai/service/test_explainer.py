import json
import os
import socket
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import pytest

from contracts import Fact
from explainer import MAX_ANSWER_TOKENS, LangChainExplainer, build_messages
from ollama_client import (
    ModelNotFound,
    OllamaBadResponse,
    OllamaSettings,
    OllamaTimeout,
    OllamaUnavailable,
    Readiness,
    check_ready,
)

QUESTION = "Bu ay ne kadar harcadım?"
FACTS = [Fact(label="Dönem", text="1-10 Ekim 2026"), Fact(label="Toplam harcama", text="735,00 TL")]


def chunk(text: str) -> dict:
    return {"model": "qwen3.5:4b", "created_at": "2026-10-19T00:00:00Z",
            "message": {"role": "assistant", "content": text}, "done": False}


def final(done_reason: str = "stop") -> dict:
    return {"model": "qwen3.5:4b", "created_at": "2026-10-19T00:00:00Z",
            "message": {"role": "assistant", "content": ""}, "done": True, "done_reason": done_reason,
            "load_duration": 5_200_000_000, "total_duration": 5_600_000_000, "eval_count": 3}


class FakeOllamaStream:
    """Answers /api/chat the way Ollama streams: one JSON object per line, with optional pauses."""

    def __init__(self, lines: list[dict | bytes] | None = None, status: int = 200, error: str = "",
                 gap: float = 0.0, first_delay: float = 0.0):
        self.lines = lines if lines is not None else [chunk("Merhaba"), final()]
        self.status, self.error, self.gap, self.first_delay = status, error, gap, first_delay
        self.requests: list[tuple[str, dict]] = []
        fake = self

        class Handler(BaseHTTPRequestHandler):
            def do_POST(self):
                length = int(self.headers.get("Content-Length", 0))
                fake.requests.append((self.path, json.loads(self.rfile.read(length))))
                time.sleep(fake.first_delay)
                try:
                    self.send_response(fake.status)
                    self.send_header("Content-Type", "application/x-ndjson" if fake.status == 200 else "application/json")
                    self.end_headers()
                    if fake.status != 200:
                        self.wfile.write(json.dumps({"error": fake.error}).encode())
                        return
                    for line in fake.lines:
                        data = line if isinstance(line, bytes) else json.dumps(line).encode()
                        self.wfile.write(data + b"\n")
                        self.wfile.flush()
                        time.sleep(fake.gap)
                except (BrokenPipeError, ConnectionResetError):
                    pass  # the client stopped reading (timeout tests); that is the point

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


def explainer_for(url: str, model: str = "qwen3.5:4b", timeout: float = 5.0) -> LangChainExplainer:
    return LangChainExplainer(OllamaSettings(base_url=url, model=model, timeout_seconds=timeout))


def closed_port_url() -> str:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return f"http://127.0.0.1:{s.getsockname()[1]}"


def test_request_has_thinking_off_temperature_zero_and_token_cap():
    with FakeOllamaStream() as fake:
        explainer_for(fake.url).explain(QUESTION, FACTS)
    path, body = fake.requests[0]
    assert path == "/api/chat"
    assert body["model"] == "qwen3.5:4b"
    assert body["think"] is False
    assert body["options"]["temperature"] == 0
    assert body["options"]["num_predict"] == MAX_ANSWER_TOKENS == 400
    assert [{"role": m["role"], "content": m["content"]} for m in body["messages"]] == build_messages(QUESTION, FACTS)


def test_streamed_parts_are_joined_and_trimmed():
    lines = [chunk("  Bu ay "), chunk("735,00 TL"), chunk(" harcadınız.\n"), final()]
    with FakeOllamaStream(lines) as fake:
        assert explainer_for(fake.url).explain(QUESTION, FACTS) == "Bu ay 735,00 TL harcadınız."


def test_404_is_model_not_found_and_names_the_model():
    with FakeOllamaStream(status=404, error="model 'yok:1b' not found") as fake:
        with pytest.raises(ModelNotFound, match="yok:1b"):
            explainer_for(fake.url, model="yok:1b").explain(QUESTION, FACTS)


def test_500_is_unavailable():
    with FakeOllamaStream(status=500, error="model requires more system memory") as fake:
        with pytest.raises(OllamaUnavailable, match="more system memory"):
            explainer_for(fake.url).explain(QUESTION, FACTS)


def test_400_is_bad_response():
    with FakeOllamaStream(status=400, error="invalid options") as fake:
        with pytest.raises(OllamaBadResponse, match="invalid options"):
            explainer_for(fake.url).explain(QUESTION, FACTS)


def test_closed_port_is_unavailable():
    with pytest.raises(OllamaUnavailable):
        explainer_for(closed_port_url()).explain(QUESTION, FACTS)


def test_no_first_part_in_time_is_timeout():
    with FakeOllamaStream(first_delay=2.0) as fake:
        started = time.monotonic()
        with pytest.raises(OllamaTimeout):
            explainer_for(fake.url, timeout=0.5).explain(QUESTION, FACTS)
    assert time.monotonic() - started < 1.5


def test_total_time_is_limited_even_while_parts_keep_coming():
    # The gap between parts (0.3 s) never reaches the 1 s limit, so only a total deadline can stop this ~6 s answer.
    lines = [chunk(f"{i} ") for i in range(20)] + [final()]
    with FakeOllamaStream(lines, gap=0.3) as fake:
        started = time.monotonic()
        with pytest.raises(OllamaTimeout):
            explainer_for(fake.url, timeout=1.0).explain(QUESTION, FACTS)
    assert time.monotonic() - started < 2.0


def test_answer_cut_at_the_token_cap_is_rejected():
    with FakeOllamaStream([chunk("Bu ay 735,00 TL harcadınız ve"), final(done_reason="length")]) as fake:
        with pytest.raises(OllamaBadResponse, match="400"):
            explainer_for(fake.url).explain(QUESTION, FACTS)


def test_broken_stream_line_is_bad_response():
    with FakeOllamaStream([chunk("Bu ay "), b"{not json", final()]) as fake:
        with pytest.raises(OllamaBadResponse):
            explainer_for(fake.url).explain(QUESTION, FACTS)


def _real_settings(**changes) -> OllamaSettings:
    base = OllamaSettings.from_env({"BANKINGAI_LLM_MODEL": "qwen3.5:4b", **os.environ})
    return OllamaSettings(**{**base.__dict__, **changes})


def _real_ollama_ready() -> bool:
    try:
        return check_ready(_real_settings()) == Readiness.READY
    except Exception:
        return False


real_ollama = pytest.mark.skipif(not _real_ollama_ready(), reason="Ollama is not running or the model is not installed")


@real_ollama
def test_real_model_explains_in_turkish():
    assert LangChainExplainer(_real_settings()).explain(QUESTION, FACTS).strip()


@real_ollama
def test_real_timeout_leaves_the_server_free():
    LangChainExplainer(_real_settings()).explain(QUESTION, FACTS)  # load the model first
    # A normal answer takes ~2 s; cutting it at 0.5 s does not depend on what the prompt lets the model write.
    # (Until AI-05 this used a "write 1 to 500" fact; the AI-05 prompt rightly treats that as data, not a task.)
    with pytest.raises(OllamaTimeout):
        LangChainExplainer(_real_settings(timeout_seconds=0.5)).explain(QUESTION, FACTS)
    # If Ollama kept generating, this call would queue behind it.
    started = time.monotonic()
    LangChainExplainer(_real_settings()).explain(QUESTION, FACTS)
    assert time.monotonic() - started < 10
