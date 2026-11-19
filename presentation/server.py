"""The presentation's remote (DEMO-04 PR2): serves the slides and turns a click on a slide into a link the simulator opens.

A browser page cannot reach the simulator, so a click posts `{"ask": "..."}` or `{"tab": "..."}` here and this turns it into
`compass://ask?q=...` or `compass://tab/<name>` for `xcrun simctl openurl booted`. It listens on 127.0.0.1 only, accepts
requests from its own page only (another site open in the same browser cannot drive the simulator), runs no shell and knows
two commands; the app reads the same two (App/DemoLink.swift) and ignores everything else.

    python3 presentation/server.py            # http://127.0.0.1:7300
"""

import json
import subprocess
import sys
from collections.abc import Callable
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import quote

HOST = "127.0.0.1"
PORT = 7300
TABS = frozenset({"home", "analysis", "assistant", "profile"})
# The API's limit for one message, counted the same way (Unicode characters).
MAX_LENGTH = 500
ALLOWED_ORIGINS: set[str] = {f"http://{HOST}:{PORT}", f"http://localhost:{PORT}"}
MAX_BODY = 4096


class SimulatorError(Exception):
    """The link could not be opened: no booted simulator, or Xcode's tools are missing."""


def link_for(command: object) -> str | None:
    """The app link for one command from a slide, or None for anything that is not exactly one known command."""
    if not isinstance(command, dict) or len(command) != 1:
        return None
    if isinstance(command.get("ask"), str):
        question = command["ask"].strip()
        if not question or len(question) > MAX_LENGTH:
            return None
        # Nothing left unencoded, so "&" or "=" in a question cannot add a parameter to the link.
        return "compass://ask?q=" + quote(question, safe="")
    if command.get("tab") in TABS:
        return f"compass://tab/{command['tab']}"
    return None


def open_in_simulator(url: str) -> None:
    """Opens the link in the booted simulator; the arguments go to simctl as a list, never through a shell."""
    try:
        result = subprocess.run(["xcrun", "simctl", "openurl", "booted", url], capture_output=True, text=True, timeout=10)
    except (OSError, subprocess.TimeoutExpired) as error:
        raise SimulatorError(str(error)) from error
    if result.returncode != 0:
        raise SimulatorError(result.stderr.strip() or f"simctl exit {result.returncode}")


def make_handler(open_url: Callable[[str], None], root: Path) -> type[BaseHTTPRequestHandler]:
    """The request handler: `GET /` is the slides, `POST /api/remote` one command; every other path is a 404."""
    page = root / "index.html"

    class Handler(BaseHTTPRequestHandler):
        def do_GET(self) -> None:
            if self.path not in ("/", "/index.html"):
                self.answer(404, {"error": "Yok."})
                return
            body = page.read_bytes()
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Cache-Control", "no-store")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_POST(self) -> None:
            if self.path != "/api/remote":
                self.answer(404, {"error": "Yok."})
                return
            # A page on another site can post here too; only our own page names our origin.
            if self.headers.get("Origin") not in ALLOWED_ORIGINS:
                self.answer(403, {"error": "Yalnız sunum sayfası gönderebilir."})
                return
            length = int(self.headers.get("Content-Length") or 0)
            if length <= 0 or length > MAX_BODY:
                self.answer(400, {"error": "Komut okunamadı."})
                return
            try:
                command = json.loads(self.rfile.read(length))
            except ValueError:
                self.answer(400, {"error": "Komut okunamadı."})
                return
            url = link_for(command)
            if url is None:
                self.answer(400, {"error": "Bilinmeyen komut."})
                return
            try:
                open_url(url)
            except SimulatorError as error:
                self.log_message("simctl: %s", error)
                self.answer(502, {"error": "Simülatör açık değil ya da bağlantıyı açamadı."})
                return
            self.answer(200, {"ok": True})

        def answer(self, status: int, body: dict) -> None:
            data = json.dumps(body, ensure_ascii=False).encode()
            self.send_response(status)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def log_message(self, format: str, *args: object) -> None:
            # Questions are the customer's words; the log keeps the method and status only.
            if args and isinstance(args[0], str) and args[0].startswith(("GET", "POST")):
                args = (args[0].split(" ")[0] + " " + self.path.split("?")[0], *args[1:])
            sys.stderr.write("present: " + (format % args) + "\n")

    return Handler


def main() -> None:
    httpd = ThreadingHTTPServer((HOST, PORT), make_handler(open_in_simulator, Path(__file__).parent))
    print(f"present: http://{HOST}:{PORT}  (Ctrl-C stops)", flush=True)
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
