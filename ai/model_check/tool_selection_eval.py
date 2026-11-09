"""Runs the fixed tool-selection cases through the AI service's real selector and prints a scored report (MCP-b K7-A).

Usage (from the repository root, Ollama and the .NET MCP server running, .env loaded):
    ai/.venv/bin/python ai/model_check/tool_selection_eval.py [--model qwen3.5:4b]

It measures the selector, the tool-selection prompt and the tool definitions the MCP server publishes right now (listed
with a locally signed demo token, as the chat lists them with the user's), so running it before and after a change
gives the comparison. Before MCP-b the definitions were Python's own (12/12, 20 Oct 2026); MCP-08 added cases S13-S26.
CHAT-03 adds the grounding check of the chat: a "clarify" case passes only when the turn would ask back instead of
guessing (S27-S31), and every other case fails if the check would ask a needless question. CHAT-02 adds follow-ups
(F01-F14): the case's previous call is given to the selector when the message counts as a follow-up, and the grounding check
accepts what was carried over unchanged, as in the chat. MCP-17/18 add the budget and savings cases (S35-S47, F15-F16).
CHAT-01 routes every case as the chat does (word rules, the model's group only without a matching rule) and shows the model
only the route's tools; --no-routing measures the full list as before, and R01-R08 are wordings the rules were not written
from. --pause waits between cases, for a machine that heats up under a long run.
CHAT-09 runs every case through the real ChatService (phrasings.run_variant): the chat's own routing, code rules, dates,
grounding and questions back decide, and the turn stops where the MCP call would be made, so this eval no longer keeps a copy of
the chat's steps. A case passes when the turn ends in exactly the expected call (merchant names without case), in a question
back for "clarify", or in a fixed reply without a tool for null.
"""

import argparse
import json
import os
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "service"))

import chat  # noqa: E402
import prompts  # noqa: E402
from mcp_gateway import McpSettings, McpToolGateway  # noqa: E402
from ollama_client import OllamaError, OllamaSettings  # noqa: E402
from phrasings import Outcome, Variant, matches, run_variant  # noqa: E402
from routing import GENERAL, PLANNING, WEALTH, Route, Router  # noqa: E402

CASES_FILE = Path(__file__).with_name("tool_selection_cases.json")
# null: no tool may run; the chat then answers with one of its fixed texts.
NO_TOOL = ["out_of_scope", "advice"]


class RecordingRouter:
    """The chat's router, remembering the route of the last message for the report."""

    def __init__(self, router):
        self.router, self.last = router, Route(())

    def route(self, message, previous_tool, deadline):
        self.last = self.router.route(message, previous_tool, deadline)
        return self.last


class EveryTool:
    """--no-routing: every group, no out-of-scope rule, as before CHAT-01."""

    def route(self, message, previous_tool, deadline):
        return Route((GENERAL, PLANNING, WEALTH), source="none")


def as_variant(case: dict) -> Variant:
    """A fixed case as a one-message phrasing."""
    expected = NO_TOOL if case["expected"] is None else case["expected"]
    return Variant(case["id"], "case", case["id"], case["message"], expected, None, "plain", True, case.get("context"),
                   (case["message"],))


def demo_token() -> str:
    """A token for Ada signed with the local key; listing needs one, and the tools list is the same for everyone."""
    import jwt  # PyJWT comes with the mcp package

    now = int(time.time())
    return jwt.encode({"sub": "10000000-0000-0000-0000-000000000001", "iss": "BankingAI.Api", "aud": "BankingAI",
                       "iat": now, "nbf": now, "exp": now + 600}, os.environ["BANKINGAI_JWT_SIGNING_KEY"], algorithm="HS256")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--model", help="Ollama model (default: BANKINGAI_LLM_MODEL or qwen3.5:4b)")
    parser.add_argument("--no-routing", action="store_true", help="show the model every tool, as before CHAT-01")
    parser.add_argument("--pause", type=float, default=0.0, help="seconds to wait before each case")
    args = parser.parse_args()

    model = args.model or os.environ.get("BANKINGAI_LLM_MODEL") or "qwen3.5:4b"
    settings = OllamaSettings.from_env({**os.environ, "BANKINGAI_LLM_MODEL": model})
    definitions = McpToolGateway(McpSettings.from_env(os.environ)).list_tools(demo_token(), time.monotonic() + 10)
    selector = chat.LangChainToolSelector(settings)
    router = RecordingRouter(EveryTool() if args.no_routing else Router(chat.LangChainIntentClassifier(settings)))
    interpreter = chat.LangChainDateInterpreter(settings)
    data = json.loads(CASES_FILE.read_text(encoding="utf-8"))

    routing = "no routing" if args.no_routing else f"routing, prompt {prompts.INTENT_PROMPT_VERSION}"
    print(f"## Tool-selection eval: {model}, prompt {prompts.TOOL_PROMPT_VERSION}, {len(definitions)} tools, {routing}\n")
    print("| Case | Result | Time (s) | Route | Outcome |")
    print("|---|---|---|---|---|")
    passed, total_time = 0, 0.0
    for case in data["cases"]:
        time.sleep(args.pause)
        started = time.monotonic()
        router.last = Route(())
        variant = as_variant(case)
        try:
            outcome = run_variant(variant, selector=selector, tools=definitions, router=router, interpreter=interpreter)
            ok = matches(variant.expected, outcome)
        except OllamaError as error:
            outcome, ok = Outcome(f"{type(error).__name__}: {error}"), False
        passed += ok
        seconds = time.monotonic() - started
        total_time += seconds
        route = router.last
        where = ("full" if args.no_routing else "out of scope" if route.out_of_scope
                 else f"{'+'.join(route.groups)} ({route.source})" if route.groups else "-")
        got = f"{outcome.tool}({json.dumps(outcome.arguments, ensure_ascii=False)})" if outcome.kind == "call" else outcome.kind
        print(f"| {case['id']} | {'pass' if ok else 'FAIL'} | {seconds:.1f} | {where} | {got} |")
    print(f"\n**{passed}/{len(data['cases'])}**, average {total_time / len(data['cases']):.1f} s per case")
    return 0 if passed == len(data["cases"]) else 1


if __name__ == "__main__":
    sys.exit(main())
