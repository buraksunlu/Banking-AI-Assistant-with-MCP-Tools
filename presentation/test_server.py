"""Tests for the presentation's remote (DEMO-04 PR2). No simulator: the command that opens a link is a fake."""

import json
import threading
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


class RemoteTests(unittest.TestCase):
    def setUp(self):
        self.opened: list[str] = []
        self.fail = False

        def open_url(url: str) -> None:
            if self.fail:
                raise server.SimulatorError("no booted simulator")
            self.opened.append(url)

        self.httpd = ThreadingHTTPServer(("127.0.0.1", 0), server.make_handler(open_url, ROOT))
        self.base = f"http://127.0.0.1:{self.httpd.server_address[1]}"
        server.ALLOWED_ORIGINS.add(self.base)
        threading.Thread(target=self.httpd.serve_forever, daemon=True).start()

    def tearDown(self):
        self.httpd.shutdown()
        self.httpd.server_close()
        server.ALLOWED_ORIGINS.discard(self.base)

    def post(self, body, origin: str | None = "same") -> tuple[int, dict]:
        data = body if isinstance(body, bytes) else json.dumps(body).encode()
        request = urllib.request.Request(f"{self.base}/api/remote", data=data, method="POST",
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
        self.fail = True
        status, body = self.post({"tab": "home"})
        self.assertEqual(status, 502)
        self.assertIn("simülatör", body["error"].lower())

    def test_only_the_slides_are_served(self):
        status, page = self.get("/")
        self.assertEqual(status, 200)
        self.assertIn("<title>", page)
        for path in ("/server.py", "/../todo.md", "/%2e%2e/todo.md", "/index.html/x"):
            with self.subTest(path=path):
                self.assertEqual(self.get(path)[0], 404)


if __name__ == "__main__":
    unittest.main()
