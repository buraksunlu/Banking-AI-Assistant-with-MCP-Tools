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
A case passes when the model
chose exactly the expected tool with exactly the expected arguments (merchant names without case), or no tool when none
may be chosen.
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
from conversation import LastCall, is_follow_up  # noqa: E402
from grounding import check_arguments  # noqa: E402
from mcp_gateway import McpSettings, McpToolGateway  # noqa: E402
from ollama_client import OllamaError, OllamaSettings  # noqa: E402
from routing import Route, Router, hide_dates, narrow  # noqa: E402
from wealth import MONTHLY_TOOL, advised_saving_call, plan_monthly  # noqa: E402

CASES_FILE = Path(__file__).with_name("tool_selection_cases.json")


def asks_back(calls: list, schemas: dict, message: str, previous: dict | None = None) -> bool:
    """True when the chat would ask back: one published tool whose period or count the user did not say (CHAT-03) and,
    in a follow-up, did not carry over either (CHAT-02)."""
    return len(calls) == 1 and calls[0].tool in schemas and \
        check_arguments(calls[0].arguments, schemas[calls[0].tool], message, previous).missing is not None


def matches(expected: dict | list | None, calls: list) -> bool:
    """Exactly one expected call (or none), with exactly the expected arguments; a list gives the equally right answers
    (an optional argument left out or set to its default)."""
    if isinstance(expected, list):
        return any(matches(choice, calls) for choice in expected)
    if expected is None:
        return not calls
    if len(calls) != 1 or calls[0].tool != expected["tool"]:
        return False
    # An optional argument written as null is left out (MCP-17/18): the MCP server publishes null for them and accepts it.
    fold = lambda arguments: {k: v.casefold() if k == "merchant" and isinstance(v, str) else v
                              for k, v in arguments.items() if v is not None}
    return fold(calls[0].arguments) == fold(expected["arguments"])


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
    # As the chat shows them: the date range of the period tools is the code's, never the model's (CHAT-08).
    definitions = hide_dates(McpToolGateway(McpSettings.from_env(os.environ)).list_tools(demo_token(), time.monotonic() + 10))
    selector = chat.LangChainToolSelector(settings)
    router = Router(chat.LangChainIntentClassifier(settings))
    schemas = {d["function"]["name"]: d["function"].get("parameters", {}) for d in definitions}
    data = json.loads(CASES_FILE.read_text(encoding="utf-8"))

    routing = "no routing" if args.no_routing else f"routing, prompt {prompts.INTENT_PROMPT_VERSION}"
    print(f"## Tool-selection eval: {model}, prompt {prompts.TOOL_PROMPT_VERSION}, {len(definitions)} tools, {routing}\n")
    print("| Case | Result | Time (s) | Route | Chosen |")
    print("|---|---|---|---|---|")
    passed, total_time = 0, 0.0
    for case in data["cases"]:
        time.sleep(args.pause)
        started = time.monotonic()
        route = Route(())
        try:
            given = case.get("context")
            context = LastCall(given["tool"], given["arguments"], given["question"]) \
                if given and is_follow_up(case["message"]) else None
            deadline = time.monotonic() + settings.timeout_seconds
            shown = definitions
            if not args.no_routing:
                route = router.route(case["message"], context.tool if context else None, deadline)
                shown = [] if route.out_of_scope else narrow(definitions, route)
            # Out of scope by rule: the chat answers without the model, which is "no tool" here.
            calls = selector.select(case["message"], shown, deadline, context) if shown else []
            # As the chat runs it (INC-03 PR2): code chooses "the amount you advised" from the words, and an optional argument the
            # user did not say (a category the model invented) is dropped before the call.
            if (advised := advised_saving_call(case["message"], context)) is not None:
                calls = [chat.ToolCall(*advised)]
            elif len(calls) == 1 and calls[0].tool == MONTHLY_TOOL:
                # The monthly saving is built from the user's words, never from the model's figures (K8-A, K17-B).
                plan = plan_monthly(calls[0].arguments, case["message"], context is not None and context.tool == "get_spending_advice")
                calls = [chat.ToolCall(MONTHLY_TOOL, plan.run)] if plan.run is not None else calls
            elif len(calls) == 1 and calls[0].tool in schemas:
                grounded = check_arguments(calls[0].arguments, schemas[calls[0].tool], case["message"],
                                           context.arguments if context else None)
                if grounded.missing is None:
                    calls = [chat.ToolCall(calls[0].tool, grounded.arguments)]
            chosen = ", ".join(f"{c.tool}({json.dumps(c.arguments, ensure_ascii=False)})" for c in calls) or "(no tool)"
            asked = asks_back(calls, schemas, case["message"], context.arguments if context else None)
            if asked:
                chosen += " → geri soru"
            ok = asked if case["expected"] == "clarify" else (matches(case["expected"], calls) and not asked)
        except OllamaError as error:
            chosen, ok = f"{type(error).__name__}: {error}", False
        passed += ok
        seconds = time.monotonic() - started
        total_time += seconds
        where = ("full" if args.no_routing else "out of scope" if route.out_of_scope
                 else f"{'+'.join(route.groups)} ({route.source})")
        print(f"| {case['id']} | {'pass' if ok else 'FAIL'} | {seconds:.1f} | {where} | {chosen} |")
    print(f"\n**{passed}/{len(data['cases'])}**, average {total_time / len(data['cases']):.1f} s per case")
    return 0 if passed == len(data["cases"]) else 1


if __name__ == "__main__":
    sys.exit(main())
