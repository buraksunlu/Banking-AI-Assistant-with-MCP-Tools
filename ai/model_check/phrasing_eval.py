"""Runs a sample of the phrasings through the chat with the real model and prints a report by intent and by transformation
(CHAT-09 PR1, K1-B).

Usage (from the repository root, Ollama and the .NET MCP server running, .env loaded):
    ai/.venv/bin/python ai/model_check/phrasing_eval.py [--per-intent 5] [--seed 9] [--pause 1] [--typos]
    ai/.venv/bin/python ai/model_check/phrasing_eval.py --refresh-tools     # rewrite mcp_tools.json from the server, then stop

The model-free test (test_phrasings.py) answers "does the code let the right answer through?"; this answers "does the model
choose right?" for the same phrasings: the real router (with the model's intent question), selector and date interpreter run
inside the real ChatService, and the turn stops where the MCP call would be made. It refuses to run when the published tools
differ from the snapshot the model-free test uses. A phrasing the model-free run already fails is marked "gap": its failure
here is not the model's.
"""

import argparse
import json
import os
import sys
import time
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "service"))

import chat  # noqa: E402
import prompts  # noqa: E402
from mcp_gateway import McpSettings, McpToolGateway  # noqa: E402
from ollama_client import OllamaError, OllamaSettings  # noqa: E402
from phrasings import GAPS_FILE, TOOLS_FILE, Outcome, expand, load, load_tools, matches, run_variant, sample  # noqa: E402
from routing import Router  # noqa: E402
from tool_selection_eval import demo_token  # noqa: E402


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--model", help="Ollama model (default: BANKINGAI_LLM_MODEL or qwen3.5:4b)")
    parser.add_argument("--per-intent", type=int, default=5, help="phrasings per intent (default 5)")
    parser.add_argument("--seed", type=int, default=9, help="the sample's seed (default 9)")
    parser.add_argument("--pause", type=float, default=0.0, help="seconds to wait before each phrasing")
    parser.add_argument("--typos", action="store_true", help="add one report-only typo phrasing per intent")
    parser.add_argument("--refresh-tools", action="store_true", help="write the published tools to mcp_tools.json and stop")
    args = parser.parse_args()

    live = McpToolGateway(McpSettings.from_env(os.environ)).list_tools(demo_token(), time.monotonic() + 10)
    if args.refresh_tools:
        TOOLS_FILE.write_text(json.dumps({"description": "The tools the .NET MCP server publishes (tools/list), in the model's tool "
                                          "shape; written by phrasing_eval.py --refresh-tools.", "tools": live},
                                         ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
        print(f"{TOOLS_FILE.name}: {len(live)} tools")
        return 0
    if live != load_tools():
        print(f"The published tools differ from {TOOLS_FILE.name}; run --refresh-tools and the model-free test first.")
        return 2

    model = args.model or os.environ.get("BANKINGAI_LLM_MODEL") or "qwen3.5:4b"
    settings = OllamaSettings.from_env({**os.environ, "BANKINGAI_LLM_MODEL": model})
    selector = chat.LangChainToolSelector(settings)
    router = Router(chat.LangChainIntentClassifier(settings))
    interpreter = chat.LangChainDateInterpreter(settings)
    gaps = json.loads(GAPS_FILE.read_text(encoding="utf-8"))["gaps"]

    variants = expand(load(), typos=args.typos)
    chosen = sample(variants, args.per_intent, args.seed)
    if args.typos:
        chosen += sample(variants, 1, args.seed, gating=False)

    print(f"## Phrasing eval: {model}, prompt {prompts.TOOL_PROMPT_VERSION}, intent {prompts.INTENT_PROMPT_VERSION}, "
          f"{len(chosen)} phrasings (seed {args.seed})\n")
    print("| Phrasing | Result | Time (s) | Message | Outcome |")
    print("|---|---|---|---|---|")
    by_intent, by_transform = defaultdict(lambda: [0, 0]), defaultdict(lambda: [0, 0])
    passed = total_time = 0
    for variant in chosen:
        time.sleep(args.pause)
        started = time.monotonic()
        try:
            outcome = run_variant(variant, selector=selector, tools=live, router=router, interpreter=interpreter)
            ok = matches(variant.expected, outcome)
        except OllamaError as error:
            outcome, ok = Outcome(type(error).__name__), False
        seconds = time.monotonic() - started
        total_time += seconds
        if variant.gating:
            passed += ok
            for table, key in ((by_intent, variant.intent), (by_transform, variant.transform)):
                table[key][0] += ok
                table[key][1] += 1
        result = "pass" if ok else ("gap" if variant.id in gaps else "FAIL")
        result += "" if variant.gating else " (typo)"
        got = f"{outcome.tool}({json.dumps(outcome.arguments, ensure_ascii=False)})" if outcome.kind == "call" else outcome.kind
        print(f"| {variant.id} | {result} | {seconds:.1f} | {' / '.join(variant.turns)} | {got} |")

    gating = sum(v.gating for v in chosen)
    print(f"\n**{passed}/{gating}** gating, average {total_time / len(chosen):.1f} s per phrasing\n")
    for title, table in (("Intent", by_intent), ("Transformation", by_transform)):
        print(f"| {title} | Passed |\n|---|---|")
        for key, (ok, count) in table.items():
            print(f"| {key} | {ok}/{count} |")
        print()
    return 0 if passed == gating else 1


if __name__ == "__main__":
    sys.exit(main())
