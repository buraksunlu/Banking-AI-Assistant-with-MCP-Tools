"""Words the two choice questions of CHAT-09 PR3 with the real model and prints a scored report (K20-B, prompt clarify-v2).

Usage (from the repository root, Ollama running, BANKINGAI_LLM_MODEL set; the MCP server is not needed):
    ai/.venv/bin/python ai/model_check/question_eval.py [--model qwen3.5:4b] [--pause 1]

Each message fits two calculations: a "what if" (one-off or monthly saving) or a planning question (budget or saving). The model
words the question in the user's words; it passes when ``clarification.check_question`` accepts it (it asks, names both choices
by their label or their words, adds no choice or figure). A failed question is never shown: the chat falls back to the fixed text, so this measures how often the user gets the
worded one. Threshold: 8 of 10; below it the prompt is changed (and its version bumped) before the PR, or the fixed text stays.
"""

import argparse
import os
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "service"))

import chat  # noqa: E402
import prompts  # noqa: E402
from clarification import ask_scenario, ask_topic, check_question  # noqa: E402
from ollama_client import OllamaError, OllamaSettings  # noqa: E402

CASES = [
    ask_scenario("Biriktirseydim ne olurdu?"),
    ask_scenario("Dolar alsaydım ne olurdu?"),
    ask_scenario("Altın olarak tutsaydım ne olurdu?"),
    ask_scenario("Her ay harcadığım kadar biriktirseydim ne olurdu?"),
    ask_scenario("Geçen ay 5.000 TL biriktirseydim ne olurdu?"),
    ask_scenario("paramı euro yapsaydım nolurdu"),
    ask_topic("Bütçemi kısıp tasarruf etsem?"),
    ask_topic("Plan yapalım mı?"),
    ask_topic("Harcamalarımı kontrol altına alsam?"),
    ask_topic("butceme gore ne kadar biriktiririm"),
]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--model", help="Ollama model (default: BANKINGAI_LLM_MODEL or qwen3.5:4b)")
    parser.add_argument("--pause", type=float, default=0.0, help="seconds to wait before each case")
    args = parser.parse_args()
    model = args.model or os.environ.get("BANKINGAI_LLM_MODEL") or "qwen3.5:4b"
    settings = OllamaSettings.from_env({**os.environ, "BANKINGAI_LLM_MODEL": model})
    writer = chat.LangChainClarificationWriter(settings)

    print(f"## Question eval: {model}, prompt {prompts.CLARIFY_PROMPT_VERSION}\n")
    print("| Message | Result | Time (s) | Question |")
    print("|---|---|---|---|")
    passed = 0
    for pending in CASES:
        time.sleep(args.pause)
        began = time.monotonic()
        try:
            text = writer.write(pending.question, pending.kind, [o.label for o in pending.options], pending.numbered,
                                time.monotonic() + chat.QUESTION_SECONDS)
            ok = check_question(text, pending)
        except OllamaError as error:
            text, ok = f"{type(error).__name__}: {error}", False
        passed += ok
        shown = " ".join(text.split()).replace("|", "/")
        print(f"| {pending.question} | {'pass' if ok else 'FAIL'} | {time.monotonic() - began:.1f} | {shown} |")
    print(f"\n**{passed}/{len(CASES)}**")
    return 0 if passed >= 8 else 1


if __name__ == "__main__":
    sys.exit(main())
