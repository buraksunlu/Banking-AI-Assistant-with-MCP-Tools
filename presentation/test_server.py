"""Tests for the presentation's remote (DEMO-04 PR2) and its setup panel (DEMO-05 PR2). No simulator and no services: the command
that opens a link, the one that runs scripts/stage.sh and the one that reads the status are fakes."""

import json
import tempfile
import threading
import time
import unittest
import urllib.error
import urllib.request
from http.server import ThreadingHTTPServer
from pathlib import Path

import server

ROOT = Path(__file__).parent


class LinkTests(unittest.TestCase):
    def test_a_question_is_percent_encoded_with_its_turkish_letters(self):
        self.assertEqual(server.link_for({"ask": "  Bu ay ne kadar harcadım? "}),
                         "compass://ask?q=Bu%20ay%20ne%20kadar%20harcad%C4%B1m%3F")

    def test_an_ampersand_cannot_add_a_second_parameter(self):
        self.assertEqual(server.link_for({"ask": "A&q=B"}), "compass://ask?q=A%26q%3DB")

    def test_a_tab_is_one_of_four(self):
        for name in ("home", "analysis", "assistant", "profile"):
            self.assertEqual(server.link_for({"tab": name}), f"compass://tab/{name}")

    def test_anything_else_is_refused(self):
        refused = [{}, {"ask": ""}, {"ask": "   "}, {"ask": 5}, {"tab": "settings"}, {"tab": "../home"},
                   {"ask": "a", "tab": "home"}, {"transfer": "deniz"}, {"ask": "a" * 501}, []]
        for command in refused:
            with self.subTest(command=command):
                self.assertIsNone(server.link_for(command))

    def test_the_limit_counts_characters_like_the_api(self):
        self.assertIsNotNone(server.link_for({"ask": "ı" * 500}))


class ControlTests(unittest.TestCase):
    """Which panel commands become a scripts/stage.sh call (DEMO-05 K8-A): a fixed verb and a fixed name, nothing else."""

    def test_a_known_command_is_the_script_with_two_fixed_words(self):
        self.assertEqual(server.control_args({"start": "db"}), ["scripts/stage.sh", "start", "db"])
        self.assertEqual(server.control_args({"stop": "all"}), ["scripts/stage.sh", "stop", "all"])
        self.assertEqual(server.control_args({"install": "app"}), ["scripts/stage.sh", "install", "app"])

    def test_every_part_can_be_started(self):
        for name in ("db", "model", "services", "simulator", "app", "all"):
            with self.subTest(name=name):
                self.assertEqual(server.control_args({"start": name}), ["scripts/stage.sh", "start", name])

    def test_anything_else_runs_nothing(self):
        refused = [{"start": "db; rm -rf ~"}, {"start": "db", "x": 1}, {"restart": "db"}, {"start": 5}, {"start": "DB"},
                   {"stop": "db"}, {"stop": "simulator"}, {"install": "db"}, {"start": "../db"}, {}, [], "start db", None]
        for command in refused:
            with self.subTest(command=command):
                self.assertIsNone(server.control_args(command))


class StageTests(unittest.TestCase):
    """The runner with a stand-in stage.sh: one command at a time, the last line of how it ended, a broken status refused."""

    SCRIPT = """#!/bin/sh
case "$1" in
  status) if [ -n "$STATUS" ]; then printf '%s' "$STATUS"; else printf '{"db":"on"}'; fi ;;
  start) echo "starting $2"; sleep 0.3; echo "$2 up" ;;
  stop) echo "stopping"; echo "could not stop $2"; exit 1 ;;
esac
"""

    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        repo = Path(self.folder.name)
        (repo / "scripts").mkdir()
        script = repo / "scripts" / "stage.sh"
        script.write_text(self.SCRIPT)
        script.chmod(0o755)
        self.stage = server.Stage(repo)

    def tearDown(self):
        self.folder.cleanup()

    def wait_until_idle(self):
        for _ in range(50):
            if self.stage.status()["busy"] is None:
                return
            time.sleep(0.05)
        raise AssertionError("the command never ended")

    def test_a_second_command_waits_for_the_first(self):
        self.assertTrue(self.stage.start(["scripts/stage.sh", "start", "db"]))
        self.assertFalse(self.stage.start(["scripts/stage.sh", "stop", "all"]))
        self.assertEqual(self.stage.status()["busy"], "start db")
        self.wait_until_idle()
        self.assertEqual(self.stage.status()["last"], {"command": "start db", "ok": True, "line": "db up"})

    def test_a_failure_keeps_its_last_line(self):
        self.stage.start(["scripts/stage.sh", "stop", "all"])
        self.wait_until_idle()
        self.assertEqual(self.stage.status()["last"], {"command": "stop all", "ok": False, "line": "could not stop all"})

    def test_arguments_never_reach_a_shell(self):
        repo = Path(self.folder.name)
        self.stage.start(["scripts/stage.sh", "start", "$(touch pwned)"])
        self.wait_until_idle()
        self.assertFalse((repo / "pwned").exists())
        self.assertEqual(self.stage.status()["last"]["line"], "$(touch pwned) up")

    def test_the_parts_are_what_the_script_printed(self):
        self.assertEqual(self.stage.status(), {"parts": {"db": "on"}, "busy": None, "last": None})

    def test_a_status_that_is_not_json_is_an_error(self):
        import os
        os.environ["STATUS"] = "Traceback: not json"
        try:
            with self.assertRaises(server.StageError):
                self.stage.status()
        finally:
            del os.environ["STATUS"]


class RemoteTests(unittest.TestCase):
    def setUp(self):
        self.opened: list[str] = []
        self.simulator_down = False

        def open_url(url: str) -> None:
            if self.simulator_down:
                raise server.SimulatorError(self.simulator_down if isinstance(self.simulator_down, str) else "no booted simulator")
            self.opened.append(url)

        self.controls: list[list[str]] = []
        self.busy = False
        self.status_body: dict = {"parts": {"db": "on"}, "busy": None, "last": None}
        self.status_fails = False

        def control(args: list[str]) -> bool:
            if self.busy:
                return False
            self.controls.append(args)
            return True

        def status() -> dict:
            if self.status_fails:
                raise server.StageError("Traceback: secret-output")
            return self.status_body

        handler = server.make_handler(open_url, ROOT, control=control, status=status)
        self.httpd = ThreadingHTTPServer(("127.0.0.1", 0), handler)
        self.base = f"http://127.0.0.1:{self.httpd.server_address[1]}"
        server.ALLOWED_ORIGINS.add(self.base)
        threading.Thread(target=self.httpd.serve_forever, daemon=True).start()

    def tearDown(self):
        self.httpd.shutdown()
        self.httpd.server_close()
        server.ALLOWED_ORIGINS.discard(self.base)

    def post(self, body, origin: str | None = "same", path: str = "/api/remote") -> tuple[int, dict]:
        data = body if isinstance(body, bytes) else json.dumps(body).encode()
        request = urllib.request.Request(f"{self.base}{path}", data=data, method="POST",
                                         headers={"Content-Type": "application/json"})
        if origin is not None:
            request.add_header("Origin", self.base if origin == "same" else origin)
        try:
            with urllib.request.urlopen(request) as response:
                return response.status, json.loads(response.read())
        except urllib.error.HTTPError as error:
            return error.code, json.loads(error.read())

    def get(self, path: str) -> tuple[int, str]:
        try:
            with urllib.request.urlopen(f"{self.base}{path}") as response:
                return response.status, response.read().decode()
        except urllib.error.HTTPError as error:
            return error.code, ""

    def test_a_question_from_the_slides_opens_its_link(self):
        status, body = self.post({"ask": "Bu ay ne kadar harcadım?"})
        self.assertEqual((status, body), (200, {"ok": True}))
        self.assertEqual(self.opened, ["compass://ask?q=Bu%20ay%20ne%20kadar%20harcad%C4%B1m%3F"])

    def test_another_site_cannot_drive_the_simulator(self):
        for origin in (None, "https://example.com", "http://localhost:1"):
            with self.subTest(origin=origin):
                status, _ = self.post({"ask": "Merhaba"}, origin=origin)
                self.assertEqual(status, 403)
        self.assertEqual(self.opened, [])

    def test_an_unknown_or_broken_command_opens_nothing(self):
        self.assertEqual(self.post({"tab": "settings"})[0], 400)
        self.assertEqual(self.post(b"{not json")[0], 400)
        self.assertEqual(self.opened, [])

    def test_no_simulator_is_a_clear_error(self):
        self.simulator_down = True
        status, body = self.post({"tab": "home"})
        self.assertEqual(status, 502)
        self.assertIn("simülatör", body["error"].lower())

    def test_an_app_that_does_not_know_the_link_is_told_apart(self):
        # Found on 27 November: an app built before DEMO-04 has no compass:// scheme, and simctl says only "error 115".
        self.simulator_down = "Simulator device failed to open compass://tab/home. (LSApplicationWorkspaceErrorDomain error 115.)"
        status, body = self.post({"tab": "home"})
        self.assertEqual(status, 502)
        self.assertIn("Derle ve kur", body["error"])

    def test_a_panel_button_runs_the_script(self):
        status, body = self.post({"start": "db"}, path="/api/control")
        self.assertEqual((status, body), (202, {"ok": True}))
        self.assertEqual(self.controls, [["scripts/stage.sh", "start", "db"]])

    def test_another_site_cannot_start_anything(self):
        for origin in (None, "https://example.com"):
            with self.subTest(origin=origin):
                self.assertEqual(self.post({"start": "all"}, origin=origin, path="/api/control")[0], 403)
        self.assertEqual(self.controls, [])

    def test_an_unknown_command_runs_nothing(self):
        self.assertEqual(self.post({"start": "db; rm -rf ~"}, path="/api/control")[0], 400)
        self.assertEqual(self.post(b"{not json", path="/api/control")[0], 400)
        self.assertEqual(self.controls, [])

    def test_one_command_at_a_time(self):
        self.busy = True
        status, body = self.post({"stop": "all"}, path="/api/control")
        self.assertEqual(status, 409)
        self.assertIn("sürüyor", body["error"])

    def test_the_status_comes_as_the_script_said(self):
        status, page = self.get("/api/status")
        self.assertEqual(status, 200)
        self.assertEqual(json.loads(page), self.status_body)

    def test_a_broken_status_does_not_show_the_scripts_output(self):
        self.status_fails = True
        try:
            urllib.request.urlopen(f"{self.base}/api/status")
            raise AssertionError("expected an error")
        except urllib.error.HTTPError as error:
            self.assertEqual(error.code, 502)
            self.assertNotIn("secret-output", error.read().decode())

    def test_only_the_slides_are_served(self):
        status, page = self.get("/")
        self.assertEqual(status, 200)
        self.assertIn("<title>", page)
        for path in ("/server.py", "/../todo.md", "/%2e%2e/todo.md", "/index.html/x"):
            with self.subTest(path=path):
                self.assertEqual(self.get(path)[0], 404)


if __name__ == "__main__":
    unittest.main()
