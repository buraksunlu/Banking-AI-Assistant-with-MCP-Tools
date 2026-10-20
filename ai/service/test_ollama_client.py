import dataclasses
import json
import os
import socket
import threading
import time
import unittest
import urllib.request
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

from ollama_client import (
    DEFAULT_OLLAMA_URL,
    ChatReply,
    ModelNotFound,
    OllamaBadResponse,
    OllamaSettings,
    OllamaTimeout,
    OllamaUnavailable,
    Readiness,
    chat,
    check_ready,
    warm_up,
)

HELLO = [{"role": "user", "content": "Merhaba"}]
TAGS = {"models": [{"name": "qwen3.5:4b", "model": "qwen3.5:4b"}, {"name": "qwen3.5:9b", "model": "qwen3.5:9b"}]}
CHAT_OK = {
    "model": "qwen3.5:4b",
    "message": {"role": "assistant", "content": "Merhaba, size nasıl yardımcı olabilirim?"},
    "done": True,
    "load_duration": 5_200_000_000,
    "total_duration": 5_600_000_000,
}
# What Ollama answers to a load-only request (no prompt): the model is in memory, nothing was generated.
LOADED = {"model": "qwen3.5:4b", "created_at": "2026-10-20T12:24:38Z", "response": "", "done": True, "done_reason": "load"}


class FakeOllama:
    """A local HTTP server that answers like Ollama with a scripted response and records what it received."""

    def __init__(self, status: int = 200, body: object = CHAT_OK, delay: float = 0.0):
        self.status, self.body, self.delay = status, body, delay
        self.requests: list[tuple[str, dict | None]] = []
        fake = self

        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                fake.requests.append((self.path, None))
                self._answer()

            def do_POST(self):
                length = int(self.headers.get("Content-Length", 0))
                fake.requests.append((self.path, json.loads(self.rfile.read(length))))
                self._answer()

            def _answer(self):
                time.sleep(fake.delay)
                data = fake.body if isinstance(fake.body, bytes) else json.dumps(fake.body).encode()
                try:
                    self.send_response(fake.status)
                    self.send_header("Content-Type", "application/json")
                    self.send_header("Content-Length", str(len(data)))
                    self.end_headers()
                    self.wfile.write(data)
                except (BrokenPipeError, ConnectionResetError):
                    pass  # the client gave up (timeout test); that is the point

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


def settings(url: str, model: str = "qwen3.5:4b", timeout: float = 5.0, keep_alive: str | int = "30m") -> OllamaSettings:
    return OllamaSettings(base_url=url, model=model, timeout_seconds=timeout, keep_alive=keep_alive)


def closed_port_url() -> str:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        port = s.getsockname()[1]
    return f"http://127.0.0.1:{port}"


class SettingsTests(unittest.TestCase):
    def test_model_is_required(self):
        for env in ({}, {"BANKINGAI_LLM_MODEL": "  "}):
            with self.assertRaises(ValueError) as error:
                OllamaSettings.from_env(env)
            self.assertIn("BANKINGAI_LLM_MODEL", str(error.exception))

    def test_defaults_when_only_model_is_given(self):
        result = OllamaSettings.from_env({"BANKINGAI_LLM_MODEL": "qwen3.5:4b", "BANKINGAI_OLLAMA_URL": ""})
        self.assertEqual(result, OllamaSettings(DEFAULT_OLLAMA_URL, "qwen3.5:4b", 30.0))

    def test_url_trailing_slash_and_timeout_are_read(self):
        result = OllamaSettings.from_env({"BANKINGAI_LLM_MODEL": "qwen3.5:9b", "BANKINGAI_OLLAMA_URL": "http://ollama:11434/",
                                          "BANKINGAI_LLM_TIMEOUT_SECONDS": "45"})
        self.assertEqual(result, OllamaSettings("http://ollama:11434", "qwen3.5:9b", 45.0))

    def test_invalid_timeout_is_rejected(self):
        for value in ("abc", "0", "-5", "inf", "nan"):
            with self.subTest(value=value), self.assertRaises(ValueError) as error:
                OllamaSettings.from_env({"BANKINGAI_LLM_MODEL": "qwen3.5:4b", "BANKINGAI_LLM_TIMEOUT_SECONDS": value})
            self.assertIn("BANKINGAI_LLM_TIMEOUT_SECONDS", str(error.exception))

    def test_keep_alive_defaults_to_thirty_minutes(self):
        for env in ({}, {"BANKINGAI_LLM_KEEP_ALIVE": ""}):
            with self.subTest(env=env):
                result = OllamaSettings.from_env({"BANKINGAI_LLM_MODEL": "qwen3.5:4b", **env})
                self.assertEqual(result.keep_alive, "30m")

    def test_keep_alive_durations_are_read(self):
        for value, expected in (("90s", "90s"), ("45m", "45m"), ("2h", "2h"), (" 1h ", "1h")):
            with self.subTest(value=value):
                result = OllamaSettings.from_env({"BANKINGAI_LLM_MODEL": "qwen3.5:4b", "BANKINGAI_LLM_KEEP_ALIVE": value})
                self.assertEqual(result.keep_alive, expected)

    def test_minus_one_means_forever_and_becomes_a_number(self):
        # Ollama rejects the text "-1" with 400 ("missing unit in duration"); only the number -1 means forever.
        result = OllamaSettings.from_env({"BANKINGAI_LLM_MODEL": "qwen3.5:4b", "BANKINGAI_LLM_KEEP_ALIVE": "-1"})
        self.assertEqual(result.keep_alive, -1)
        self.assertIsInstance(result.keep_alive, int)

    def test_invalid_keep_alive_is_rejected(self):
        # "0" and "0m" would unload the model after every request, making every question a cold one.
        for value in ("0", "0m", "30", "-1m", "-5m", "1d", "abc", "30 dakika", "30m5s"):
            with self.subTest(value=value), self.assertRaises(ValueError) as error:
                OllamaSettings.from_env({"BANKINGAI_LLM_MODEL": "qwen3.5:4b", "BANKINGAI_LLM_KEEP_ALIVE": value})
            self.assertIn("BANKINGAI_LLM_KEEP_ALIVE", str(error.exception))


class ChatTests(unittest.TestCase):
    def test_answer_and_timings_are_returned(self):
        with FakeOllama() as fake:
            reply = chat(settings(fake.url), HELLO)
        self.assertEqual(reply, ChatReply("Merhaba, size nasıl yardımcı olabilirim?", 5.2, 5.6))

    def test_request_turns_thinking_off_and_is_deterministic(self):
        with FakeOllama() as fake:
            chat(settings(fake.url), HELLO)
        path, body = fake.requests[0]
        self.assertEqual(path, "/api/chat")
        self.assertEqual(body["model"], "qwen3.5:4b")
        self.assertEqual(body["messages"], HELLO)
        self.assertIs(body["stream"], False)
        self.assertIs(body["think"], False)
        self.assertEqual(body["options"]["temperature"], 0)

    def test_request_keeps_the_model_loaded(self):
        with FakeOllama() as fake:
            chat(settings(fake.url), HELLO)
        self.assertEqual(fake.requests[0][1]["keep_alive"], "30m")

    def test_404_is_model_not_found_and_names_the_model(self):
        with FakeOllama(404, {"error": "model 'yok:1b' not found"}) as fake:
            with self.assertRaises(ModelNotFound) as error:
                chat(settings(fake.url, model="yok:1b"), HELLO)
        self.assertIn("yok:1b", str(error.exception))

    def test_400_is_bad_response(self):
        with FakeOllama(400, {"error": "model is required"}) as fake:
            with self.assertRaises(OllamaBadResponse) as error:
                chat(settings(fake.url), HELLO)
        self.assertIn("model is required", str(error.exception))

    def test_500_is_unavailable(self):
        with FakeOllama(500, {"error": "model requires more system memory"}) as fake:
            with self.assertRaises(OllamaUnavailable) as error:
                chat(settings(fake.url), HELLO)
        self.assertIn("more system memory", str(error.exception))

    def test_body_that_is_not_json_is_bad_response(self):
        with FakeOllama(200, b"<html>proxy error</html>") as fake:
            with self.assertRaises(OllamaBadResponse):
                chat(settings(fake.url), HELLO)

    def test_json_without_message_text_is_bad_response(self):
        for body in ({"done": True}, {"message": {"role": "assistant"}}, ["not", "an", "object"]):
            with self.subTest(body=body), FakeOllama(200, body) as fake:
                with self.assertRaises(OllamaBadResponse):
                    chat(settings(fake.url), HELLO)

    def test_slow_server_is_timeout(self):
        with FakeOllama(delay=2.0) as fake:
            started = time.monotonic()
            with self.assertRaises(OllamaTimeout):
                chat(settings(fake.url, timeout=0.5), HELLO)
        self.assertLess(time.monotonic() - started, 1.5)

    def test_closed_port_is_unavailable(self):
        with self.assertRaises(OllamaUnavailable):
            chat(settings(closed_port_url()), HELLO)


class WarmUpTests(unittest.TestCase):
    def test_the_model_is_loaded_without_a_prompt(self):
        with FakeOllama(body=LOADED) as fake:
            seconds = warm_up(settings(fake.url))
        self.assertEqual(fake.requests, [("/api/generate", {"model": "qwen3.5:4b", "keep_alive": "30m"})])
        self.assertGreaterEqual(seconds, 0.0)

    def test_forever_is_sent_as_a_number(self):
        with FakeOllama(body=LOADED) as fake:
            warm_up(settings(fake.url, keep_alive=-1))
        self.assertEqual(json.dumps(fake.requests[0][1]["keep_alive"]), "-1")

    def test_warm_up_may_take_longer_than_a_chat_call(self):
        # Nobody waits for the warm-up, and a model read from an external disk can take longer than one chat turn.
        with FakeOllama(body=LOADED, delay=0.6) as fake:
            warm_up(settings(fake.url, timeout=0.2))

    def test_404_is_model_not_found_and_names_the_model(self):
        with FakeOllama(404, {"error": "model 'yok:1b' not found"}) as fake:
            with self.assertRaises(ModelNotFound) as error:
                warm_up(settings(fake.url, model="yok:1b"))
        self.assertIn("yok:1b", str(error.exception))

    def test_closed_port_is_unavailable(self):
        with self.assertRaises(OllamaUnavailable):
            warm_up(settings(closed_port_url()))


class CheckReadyTests(unittest.TestCase):
    def test_installed_model_is_ready(self):
        with FakeOllama(body=TAGS) as fake:
            self.assertEqual(check_ready(settings(fake.url)), Readiness.READY)
        self.assertEqual(fake.requests, [("/api/tags", None)])

    def test_name_without_tag_means_latest(self):
        # Ollama reads "qwen3.5" as "qwen3.5:latest", which is not installed here.
        with FakeOllama(body=TAGS) as fake:
            self.assertEqual(check_ready(settings(fake.url, model="qwen3.5")), Readiness.MODEL_MISSING)
        latest = {"models": [{"name": "qwen3.5:latest", "model": "qwen3.5:latest"}]}
        with FakeOllama(body=latest) as fake:
            self.assertEqual(check_ready(settings(fake.url, model="qwen3.5")), Readiness.READY)

    def test_empty_model_list_is_model_missing(self):
        # What Ollama reports when it was started while the models disk was not attached.
        with FakeOllama(body={"models": []}) as fake:
            self.assertEqual(check_ready(settings(fake.url)), Readiness.MODEL_MISSING)

    def test_unreachable_or_broken_server_is_unavailable(self):
        self.assertEqual(check_ready(settings(closed_port_url())), Readiness.SERVER_UNAVAILABLE)
        with FakeOllama(500, {"error": "boom"}) as fake:
            self.assertEqual(check_ready(settings(fake.url)), Readiness.SERVER_UNAVAILABLE)
        with FakeOllama(200, b"not json") as fake:
            self.assertEqual(check_ready(settings(fake.url)), Readiness.SERVER_UNAVAILABLE)

    def test_check_does_not_wait_for_the_chat_timeout(self):
        with FakeOllama(body=TAGS, delay=4.0) as fake:
            started = time.monotonic()
            result = check_ready(settings(fake.url, timeout=30.0))
        self.assertEqual(result, Readiness.SERVER_UNAVAILABLE)
        self.assertLess(time.monotonic() - started, 3.8)


def real_settings(**overrides) -> OllamaSettings:
    base = OllamaSettings.from_env({"BANKINGAI_LLM_MODEL": "qwen3.5:4b", **os.environ})
    return dataclasses.replace(base, **overrides)


def real_ollama_is_ready() -> bool:
    try:
        return check_ready(real_settings()) == Readiness.READY
    except Exception:
        return False


@unittest.skipUnless(real_ollama_is_ready(), "Ollama is not running or the model is not installed")
class RealOllamaTests(unittest.TestCase):
    def test_model_answers(self):
        reply = chat(real_settings(), HELLO)
        self.assertTrue(reply.content.strip())

    def test_missing_model_is_reported(self):
        self.assertEqual(check_ready(real_settings(model="yok:1b")), Readiness.MODEL_MISSING)
        with self.assertRaises(ModelNotFound):
            chat(real_settings(model="yok:1b"), HELLO)

    def test_timeout_leaves_the_server_free(self):
        chat(real_settings(), HELLO)  # load the model first, so only the generation is cut
        long_task = [{"role": "user", "content": "1'den 500'e kadar bütün sayıları tek tek yaz."}]
        with self.assertRaises(OllamaTimeout):
            chat(real_settings(timeout_seconds=0.5), long_task)
        # If Ollama kept generating ~1500 tokens (~60 s here), this call would queue behind it.
        started = time.monotonic()
        chat(real_settings(), HELLO)
        self.assertLess(time.monotonic() - started, 10)

    def test_warm_up_keeps_the_model_loaded_for_the_setting(self):
        settings = real_settings()
        urllib.request.urlopen(urllib.request.Request(  # unload first, so the warm-up really loads it
            f"{settings.base_url}/api/generate", json.dumps({"model": settings.model, "keep_alive": 0}).encode()), timeout=30).read()
        warm_up(dataclasses.replace(settings, keep_alive="30m"))
        with urllib.request.urlopen(f"{settings.base_url}/api/ps", timeout=5) as response:
            loaded = {m["name"]: m for m in json.loads(response.read())["models"]}
        self.assertIn(settings.model, loaded)
        expires = datetime.fromisoformat(loaded[settings.model]["expires_at"])
        minutes = (expires - datetime.now(timezone.utc)).total_seconds() / 60
        self.assertTrue(29 <= minutes <= 30.5, minutes)


if __name__ == "__main__":
    unittest.main()
