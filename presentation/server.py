"""The presentation's remote (DEMO-04 PR2): serves the slides and turns a click on a slide into a link the simulator opens.

A browser page cannot reach the simulator, so a click posts `{"ask": "..."}` or `{"tab": "..."}` here and this turns it into
`compass://ask?q=...` or `compass://tab/<name>` for `xcrun simctl openurl booted`. It listens on 127.0.0.1 only, accepts
requests from its own page only (another site open in the same browser cannot drive the simulator), runs no shell and knows
two commands; the app reads the same two (App/DemoLink.swift) and ignores everything else.

The setup panel (DEMO-05 PR2) posts `{"start": "db"}` and the like to `/api/control`; each known command becomes
`scripts/stage.sh <verb> <name>` with two fixed words (K8-A), one at a time, and `/api/status` reports what is running.

    python3 presentation/server.py            # http://127.0.0.1:7300
"""

import json
import subprocess
import sys
import threading
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
REPO = Path(__file__).resolve().parent.parent
STAGE_SCRIPT = "scripts/stage.sh"
# What the panel may do (K10-C, K11-A): start every part, stop only the services and the model (the database stays up),
# build and install only the app.
CONTROLS: dict[str, frozenset[str]] = {
    "start": frozenset({"db", "model", "services", "simulator", "app", "all"}),
    "stop": frozenset({"services", "model", "all"}),
    "install": frozenset({"app"}),
}


class SimulatorError(Exception):
    """The link could not be opened: no booted simulator, or Xcode's tools are missing."""


class StageError(Exception):
    """The status could not be read; the message is for the log, never for the page."""


def control_args(command: object) -> list[str] | None:
    """The stage.sh call for one panel command, or None for anything that is not exactly one known verb with one known name."""
    if not isinstance(command, dict) or len(command) != 1:
        return None
    ((verb, name),) = command.items()
    if verb not in CONTROLS or not isinstance(name, str) or name not in CONTROLS[verb]:
        return None
    return [STAGE_SCRIPT, verb, name]


class Stage:
    """Runs one stage.sh command at a time in the background and remembers how the last one ended.

    The command runs in its own session so the services it starts outlive this server (K11-A: quitting the stage leaves them
    running); its output goes to logs/stage.log, and only its last line is shown on the panel.
    """

    def __init__(self, repo: Path = REPO) -> None:
        self.repo = repo
        self.log = repo / "logs" / "stage.log"
        self.lock = threading.Lock()
        self.process: subprocess.Popen | None = None
        self.command: str | None = None
        self.last: dict | None = None

    def start(self, args: list[str]) -> bool:
        """Starts the command; False while another one is still running."""
        with self.lock:
            self._reap()
            if self.process is not None:
                return False
            self.log.parent.mkdir(exist_ok=True)
            with self.log.open("w") as output:
                self.process = subprocess.Popen(args, cwd=self.repo, stdout=output, stderr=subprocess.STDOUT,
                                                stdin=subprocess.DEVNULL, start_new_session=True)
            self.command = " ".join(args[1:])
            return True

    def status(self) -> dict:
        """The parts as `stage.sh status` sees them, the running command and how the last one ended."""
        try:
            result = subprocess.run([STAGE_SCRIPT, "status"], cwd=self.repo, capture_output=True, text=True, timeout=15)
            parts = json.loads(result.stdout)
        except (OSError, subprocess.TimeoutExpired, ValueError) as error:
            raise StageError(str(error)) from error
        if not isinstance(parts, dict):
            raise StageError("status is not an object")
        with self.lock:
            self._reap()
            return {"parts": parts, "busy": self.command if self.process else None, "last": self.last}

    def _reap(self) -> None:
        if self.process is None or self.process.poll() is None:
            return
        lines = [line for line in self.log.read_text(errors="replace").splitlines() if line.strip()] if self.log.exists() else []
        self.last = {"command": self.command, "ok": self.process.returncode == 0, "line": lines[-1][:200] if lines else ""}
        self.process = None
        self.command = None


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


def make_handler(open_url: Callable[[str], None], root: Path, control: Callable[[list[str]], bool] | None = None,
                 status: Callable[[], dict] | None = None) -> type[BaseHTTPRequestHandler]:
    """The request handler: `GET /` is the slides, `GET /api/status` the setup panel's lights, `POST /api/remote` one command
    for the simulator and `POST /api/control` one for the stage; every other path is a 404."""
    page = root / "index.html"
    if control is None or status is None:
        stage = Stage()
        control = control or stage.start
        status = status or stage.status

    class Handler(BaseHTTPRequestHandler):
        def do_GET(self) -> None:
            if self.path == "/api/status":
                try:
                    self.answer(200, status())
                except StageError as error:
                    self.log_message("status: %s", error)
                    self.answer(502, {"error": "Durum okunamadı."})
                return
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
            if self.path not in ("/api/remote", "/api/control"):
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
            if self.path == "/api/control":
                args = control_args(command)
                if args is None:
                    self.answer(400, {"error": "Bilinmeyen komut."})
                elif not control(args):
                    self.answer(409, {"error": "Önceki komut sürüyor."})
                else:
                    self.answer(202, {"ok": True})
                return
            url = link_for(command)
            if url is None:
                self.answer(400, {"error": "Bilinmeyen komut."})
                return
            try:
                open_url(url)
            except SimulatorError as error:
                self.log_message("simctl: %s", error)
                # No app on the simulator knows the link: not installed, or built before the compass:// scheme (DEMO-04).
                unknown = "LSApplicationWorkspaceErrorDomain" in str(error)
                self.answer(502, {"error": "Uygulama bu bağlantıyı tanımıyor: H → Derle ve kur." if unknown
                                  else "Simülatör açık değil ya da bağlantıyı açamadı."})
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
