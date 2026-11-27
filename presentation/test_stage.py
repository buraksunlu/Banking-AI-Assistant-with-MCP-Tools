"""scripts/stage.sh's model parts against a stand-in Ollama (DEMO-05 PR4a, K1-A K2-D K3-A K4-A).

The script is sourced, so each function runs alone: no database, simulator or real model is touched. OLLAMA_URL points it at a
small HTTP server that answers /api/tags and /api/ps as told and records what is posted to /api/generate.
"""

import json
import subprocess
import tempfile
import threading
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SCRIPT = REPO / "scripts" / "stage.sh"
MODEL = "qwen3.5:4b"


class StandInOllama:
    def __init__(self, tags: list[str], loaded: list[str]):
        self.tags, self.loaded, self.posted = tags, loaded, []
        owner = self

        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                names = {"/api/tags": owner.tags, "/api/ps": owner.loaded}.get(self.path)
                if names is None:
                    self.send_error(404)
                    return
                self._json({"models": [{"name": name, "model": name} for name in names]})

            def do_POST(self):
                body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
                owner.posted.append((self.path, body))
                owner.loaded.append(body["model"])
                self._json({"model": body["model"], "done": True, "done_reason": "load"})

            def _json(self, value):
                data = json.dumps(value).encode()
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                self.wfile.write(data)

            def log_message(self, *args):
                pass

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        self.url = f"http://127.0.0.1:{self.server.server_address[1]}"

    def close(self):
        self.server.shutdown()
        self.server.server_close()


def run(function: str, url: str, before: str = "") -> subprocess.CompletedProcess:
    script = f'source "{SCRIPT}" && {before} {function}'
    return subprocess.run(["bash", "-c", script], capture_output=True, text=True, timeout=30,
                          env={"PATH": "/usr/bin:/bin:/usr/sbin:/sbin", "HOME": str(Path.home()), "OLLAMA_URL": url,
                               "BANKINGAI_LLM_MODEL": MODEL})


class ModelStateTests(unittest.TestCase):
    def state(self, tags, loaded) -> str:
        ollama = StandInOllama(tags, loaded)
        self.addCleanup(ollama.close)
        return run("model_state", ollama.url).stdout.strip()

    def test_a_model_in_memory_is_on(self):
        self.assertEqual(self.state([MODEL, "llama3:8b"], [MODEL]), "on")

    def test_an_installed_model_that_is_not_in_memory_is_cold(self):
        # The case the light used to show green: the first question then loads it and times out (504).
        self.assertEqual(self.state([MODEL], []), "cold")

    def test_another_model_in_memory_leaves_ours_cold(self):
        self.assertEqual(self.state([MODEL, "llama3:8b"], ["llama3:8b"]), "cold")

    def test_a_model_that_is_not_installed_is_missing(self):
        self.assertEqual(self.state(["llama3:8b"], []), "missing")

    def test_no_ollama_is_off(self):
        self.assertEqual(run("model_state", "http://127.0.0.1:9").stdout.strip(), "off")


class WarmTests(unittest.TestCase):
    def test_warming_loads_the_model_to_stay_until_it_is_unloaded(self):
        ollama = StandInOllama([MODEL], [])
        self.addCleanup(ollama.close)

        result = run("warm_model", ollama.url)

        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        # No prompt: Ollama only loads (AI-15); -1 as a number, the text "-1" is refused with 400.
        self.assertEqual(ollama.posted, [("/api/generate", {"model": MODEL, "keep_alive": -1})])
        self.assertEqual(run("model_state", ollama.url).stdout.strip(), "on")

    def test_the_ai_service_the_stage_starts_never_unloads_the_model(self):
        # nohup stands in for the start and writes the setting the service would get; the log goes to a folder of the test's own,
        # never to logs/, where the running service writes.
        with tempfile.TemporaryDirectory() as logs:
            run("start_ai; wait", "http://127.0.0.1:9", before=f'LOGS="{logs}"; nohup() {{ echo "keep=$BANKINGAI_LLM_KEEP_ALIVE"; }};')

            self.assertEqual((Path(logs) / "live-ai.log").read_text().strip(), "keep=-1")


class StartModelTests(unittest.TestCase):
    """Başlat on the model row: load, then the warm-up questions when the AI service is up to take them (K3-A)."""

    STUBS = 'model_installed() { return 0; }; warm_model() { echo loaded; }; warm_chain() { echo warmed; };'

    def test_the_model_is_loaded_and_warmed_when_the_ai_service_is_up(self):
        result = run("start_model", "http://127.0.0.1:9", before=self.STUBS + " ai_on() { return 0; };")

        self.assertEqual([line for line in result.stdout.split() if line in ("loaded", "warmed")], ["loaded", "warmed"])

    def test_without_the_ai_service_the_model_is_only_loaded(self):
        result = run("start_model", "http://127.0.0.1:9", before=self.STUBS + " ai_on() { return 1; };")

        self.assertIn("loaded", result.stdout.split())
        self.assertNotIn("warmed", result.stdout.split())


if __name__ == "__main__":
    unittest.main()
