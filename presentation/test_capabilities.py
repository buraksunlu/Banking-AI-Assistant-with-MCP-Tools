"""Every question the slides can send was asked on the live chain and got the right kind of reply (DEMO-05 PR3, K14-B).

live_trial.json holds what the live chain answered (as Murat, on the live database, on the day written in it); this test reads the
page itself, so a question added to a slide without being asked live, or one that got an error, fails here. The abilities' list is
the JSON block in index.html, the same one the page draws.
"""

import json
import unittest
from pathlib import Path

from live_trial import WARM, abilities, cases, key, page, slide_chains, summary, tool_questions, warm

ROOT = Path(__file__).parent
PAGE = page()
TRIAL = json.loads((ROOT / "live_trial.json").read_text(encoding="utf-8"))
RESULTS: dict = TRIAL["results"]
REPLIES = {"answer", "clarification", "outOfScope"}
# What a family is there to show: a refusal must be one, a clarifying question must ask.
FAMILY_REPLY = {"Hatalı tarih": "clarification", "Geri soru": "clarification", "Ret ve sınır": "outOfScope"}


class CapabilityTests(unittest.TestCase):
    def test_every_ability_was_asked_live_with_the_reply_its_family_shows(self):
        data = abilities(PAGE)
        for item in data["items"]:
            if not item["steps"]:
                continue
            family = data["families"][item["f"]][0]
            with self.subTest(ability=item["title"]):
                self.assertTrue(key(item["steps"]) in RESULTS, f"not asked on the live chain: {key(item['steps'])}")
                reply = RESULTS[key(item["steps"])]["type"]
                self.assertIn(reply, REPLIES)
                if family in FAMILY_REPLY:
                    self.assertEqual(reply, FAMILY_REPLY[family])

    def test_every_slide_button_was_asked_live(self):
        for chain in slide_chains(PAGE):
            with self.subTest(question=key(chain)):
                self.assertTrue(key(chain) in RESULTS, f"not asked on the live chain: {key(chain)}")
                self.assertIn(RESULTS[key(chain)]["type"], REPLIES)

    def test_every_tool_question_reaches_its_tool_or_asks_first(self):
        tools = tool_questions(PAGE)
        self.assertEqual(len(tools), 20)
        for tool, question in tools:
            with self.subTest(tool=tool):
                result = RESULTS[question]
                if result["type"] == "answer":
                    self.assertIn(tool, result["tools"])
                else:
                    self.assertEqual(result["type"], "clarification")

    def test_every_family_has_an_ability_and_every_ability_a_family(self):
        data = abilities(PAGE)
        used = {item["f"] for item in data["items"]}
        self.assertEqual(used, set(range(len(data["families"]))))

    def test_a_tab_ability_names_a_tab_the_app_knows(self):
        for item in abilities(PAGE)["items"]:
            if "tab" in item:
                with self.subTest(ability=item["title"]):
                    self.assertIn(item["tab"], {"home", "analysis", "assistant", "profile"})
                    self.assertEqual(item["steps"], [])


class TrialTests(unittest.TestCase):
    def test_the_trial_asks_each_question_once_and_a_follow_up_with_its_chain(self):
        asked = [key(steps) for steps in cases(PAGE)]
        self.assertEqual(len(asked), len(set(asked)))
        self.assertTrue(any(" → " in question for question in asked))
        self.assertEqual(set(asked), set(RESULTS))

    def test_a_summary_keeps_the_kind_tools_and_seconds_and_never_the_answer(self):
        reply = {"type": "answer", "answer": "Kasım'da 4.218,40 TL harcadın.", "cards": [{"amount": "4218.40"}],
                 "toolCalls": [{"tool": "get_spending_summary", "arguments": {"period": "ThisMonth"}}]}
        self.assertEqual(summary(reply, 2.345), {"type": "answer", "tools": ["get_spending_summary"], "seconds": 2.3})
        self.assertEqual(summary({"type": "http 502"}, 1), {"type": "http 502", "tools": [], "seconds": 1})


class WarmTests(unittest.TestCase):
    """DEMO-05 PR4a K3 (A, after the measurement): a model just loaded spent ~25 s on each of the first two questions and on the
    first follow-up, rebuilding the tool prompt's cache; the warm-up asks each kind twice, so the audience's first one is not."""

    def test_the_warm_up_is_two_questions_and_two_follow_ups_that_were_answered_live(self):
        self.assertEqual(len(WARM), 2)
        self.assertEqual(len({chain[0] for chain in WARM}), 2)
        self.assertEqual(len({chain[1] for chain in WARM}), 2)
        for chain in WARM:
            with self.subTest(chain=key(chain)):
                self.assertTrue(chain[1].startswith("Peki "))
                self.assertEqual(RESULTS[key(chain)]["type"], "answer")

    def test_every_chain_is_asked_once_when_it_is_answered(self):
        asked = []

        warm(lambda chain: asked.append(chain) or "answer")

        self.assertEqual(asked, WARM)

    def test_a_chain_that_times_out_is_asked_again(self):
        # The first request to a cold model loads it and can end in 504: that is the model starting, not a failure.
        replies, asked = iter(["http 504", "answer", "answer"]), []

        warm(lambda chain: asked.append(chain) or next(replies))

        self.assertEqual(asked, [WARM[0], WARM[0], WARM[1]])

    def test_a_service_that_never_answers_stops_the_warm_up_with_a_reason(self):
        with self.assertRaises(SystemExit) as stop:
            warm(lambda chain: "http 502")

        self.assertIn("did not answer", str(stop.exception))


if __name__ == "__main__":
    unittest.main()
