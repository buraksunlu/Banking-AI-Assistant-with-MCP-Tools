"""Connection to the local Ollama server (AI-02).

Every way a call can go wrong ends as one of four named errors, so the AI service can tell the
user "the assistant is not available" instead of crashing. Standard library only.

Manual check (from the repository root, after loading .env):
    python3 ai/service/ollama_client.py
"""

import json
import math
import os
import sys
import urllib.error
import urllib.request
from collections.abc import Mapping
from dataclasses import dataclass
from enum import Enum

DEFAULT_OLLAMA_URL = "http://127.0.0.1:11434"
DEFAULT_TIMEOUT_SECONDS = 30.0
READY_TIMEOUT_SECONDS = 3.0


class OllamaError(Exception):
    """Base of every Ollama failure the service handles."""


class OllamaUnavailable(OllamaError):
    """The server cannot be reached or failed on its side (connection refused, 5xx)."""


class ModelNotFound(OllamaError):
    """The configured model is not installed on the server (404)."""


class OllamaTimeout(OllamaError):
    """No answer within the configured time; the server stops the work when the client leaves."""


class OllamaBadResponse(OllamaError):
    """The server answered, but not with what a chat call returns (400, not JSON, no message)."""


class Readiness(Enum):
    """What the health check found, without loading the model."""

    READY = "ready"
    SERVER_UNAVAILABLE = "server_unavailable"
    MODEL_MISSING = "model_missing"


@dataclass(frozen=True)
class OllamaSettings:
    """Where Ollama is, which model to use and how long a chat call may take."""

    base_url: str
    model: str
    timeout_seconds: float

    @classmethod
    def from_env(cls, env: Mapping[str, str]) -> "OllamaSettings":
        """Reads BANKINGAI_LLM_MODEL (required), BANKINGAI_OLLAMA_URL and BANKINGAI_LLM_TIMEOUT_SECONDS."""
        model = env.get("BANKINGAI_LLM_MODEL", "").strip()
        if not model:
            raise ValueError("BANKINGAI_LLM_MODEL is required (for example qwen3.5:4b).")

        # An empty value (as in .env.example) means "use the default", not "no address".
        base_url = (env.get("BANKINGAI_OLLAMA_URL") or DEFAULT_OLLAMA_URL).rstrip("/")

        raw_timeout = env.get("BANKINGAI_LLM_TIMEOUT_SECONDS") or str(DEFAULT_TIMEOUT_SECONDS)
        try:
            timeout = float(raw_timeout)
        except ValueError:
            timeout = math.nan
        if not (math.isfinite(timeout) and timeout > 0):
            raise ValueError(f"BANKINGAI_LLM_TIMEOUT_SECONDS must be a positive number of seconds, got {raw_timeout!r}.")

        return cls(base_url, model, timeout)


@dataclass(frozen=True)
class ChatReply:
    """The model's answer and Ollama's own timings."""

    content: str
    load_seconds: float
    total_seconds: float


def check_ready(settings: OllamaSettings) -> Readiness:
    """Asks the server which models it has; never loads a model, never waits longer than a few seconds."""
    try:
        tags = _request(settings.base_url, "/api/tags", None, min(READY_TIMEOUT_SECONDS, settings.timeout_seconds))
    except OllamaError:
        return Readiness.SERVER_UNAVAILABLE

    models = tags.get("models") if isinstance(tags, dict) else None
    if not isinstance(models, list):
        return Readiness.SERVER_UNAVAILABLE

    installed = {name for m in models if isinstance(m, dict) for name in (m.get("name"), m.get("model")) if name}
    return Readiness.READY if _with_tag(settings.model) in installed else Readiness.MODEL_MISSING


def chat(settings: OllamaSettings, messages: list[dict]) -> ChatReply:
    """Sends one non-streaming chat request (thinking off, temperature 0) and returns the answer."""
    body = {
        "model": settings.model,
        "messages": messages,
        "stream": False,
        # AI-01: thinking took 50 s for a greeting on this machine, and greedy decoding makes it loop.
        "think": False,
        "options": {"temperature": 0},
    }
    try:
        result = _request(settings.base_url, "/api/chat", body, settings.timeout_seconds)
    except ModelNotFound:
        # Ollama names the model too, but the configured name is what the operator has to fix.
        raise ModelNotFound(f"Model {settings.model!r} is not installed on {settings.base_url}.") from None

    message = result.get("message") if isinstance(result, dict) else None
    content = message.get("content") if isinstance(message, dict) else None
    if not isinstance(content, str):
        raise OllamaBadResponse(f"Chat answer has no message text: {str(result)[:200]}")

    return ChatReply(content, _seconds(result.get("load_duration")), _seconds(result.get("total_duration")))


def _request(base_url: str, path: str, body: dict | None, timeout: float):
    data = json.dumps(body).encode("utf-8") if body is not None else None
    request = urllib.request.Request(f"{base_url}{path}", data=data, headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            raw = response.read()
    # HTTPError is a kind of URLError, so it has to be caught first.
    except urllib.error.HTTPError as error:
        reason = _error_text(error)
        if error.code == 404:
            raise ModelNotFound(reason) from None
        if error.code >= 500:
            raise OllamaUnavailable(f"Ollama failed ({error.code}): {reason}") from None
        raise OllamaBadResponse(f"Ollama rejected the request ({error.code}): {reason}") from None
    except TimeoutError:
        raise OllamaTimeout(f"No answer from Ollama within {timeout:g} s.") from None
    except urllib.error.URLError as error:
        # A timeout while connecting arrives wrapped in URLError, not as a bare TimeoutError.
        if isinstance(error.reason, TimeoutError):
            raise OllamaTimeout(f"No answer from Ollama within {timeout:g} s.") from None
        raise OllamaUnavailable(f"Cannot reach Ollama at {base_url}: {error.reason}") from None
    except (ConnectionError, OSError) as error:
        raise OllamaUnavailable(f"Connection to Ollama failed: {error}") from None

    try:
        return json.loads(raw)
    except ValueError:
        raise OllamaBadResponse(f"Ollama answer is not JSON: {raw[:200]!r}") from None


def _error_text(error: urllib.error.HTTPError) -> str:
    with error:
        raw = error.read()
    try:
        return str(json.loads(raw).get("error", raw[:200]))
    except (ValueError, AttributeError):
        return repr(raw[:200])


def _with_tag(model: str) -> str:
    # Ollama's own rule: a name without a tag means the "latest" tag.
    return model if ":" in model else f"{model}:latest"


def _seconds(nanoseconds) -> float:
    return round(nanoseconds / 1e9, 3) if isinstance(nanoseconds, (int, float)) else 0.0


def main() -> int:
    try:
        settings = OllamaSettings.from_env(os.environ)
    except ValueError as error:
        print(f"Ayar hatası: {error}")
        return 2

    readiness = check_ready(settings)
    if readiness is Readiness.SERVER_UNAVAILABLE:
        print(f"Ollama çalışmıyor ya da {settings.base_url} adresinden cevap vermiyor. Ollama uygulamasını açın.")
        return 1
    if readiness is Readiness.MODEL_MISSING:
        print(f"Model yok: {settings.model}. 'ollama pull {settings.model}' çalıştırın; "
              "modeller harici diskteyse disk takılı mı ve Ollama disk takıldıktan sonra mı açıldı, kontrol edin.")
        return 1

    print(f"Hazır: {settings.model} ({settings.base_url}). Deneme sorusu gönderiliyor...")
    try:
        reply = chat(settings, [{"role": "user", "content": "Merhaba, kısaca kendini tanıt."}])
    except OllamaTimeout:
        print(f"Model {settings.timeout_seconds:g} saniyede cevap vermedi.")
        return 1
    except OllamaError as error:
        print(f"Model cevap veremedi: {error}")
        return 1

    print(f"Cevap: {reply.content.strip()}")
    print(f"Yükleme {reply.load_seconds:.1f} sn, toplam {reply.total_seconds:.1f} sn.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
