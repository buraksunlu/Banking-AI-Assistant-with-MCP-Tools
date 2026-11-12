"""Words the questions back with the real model and prints a scored report (CHAT-09 PR3 K20-B; CHAT-10 PR3, prompt clarify-v3).

Usage (from the repository root, Ollama running, BANKINGAI_LLM_MODEL set; the MCP server is not needed):
    ai/.venv/bin/python ai/model_check/question_eval.py [--model qwen3.5:4b] [--pause 1]

Each message fits two calculations: a "what if" (one-off or monthly saving) or a planning question (budget or saving). The model
words the question in the user's words; it passes when ``clarification.check_question`` accepts it (it asks, names both choices
by their label or their words, adds no choice or figure; CHAT-10 K5-B: the options are rows under it, so it names all of
them or none, and never lists them numbered). A failed question is never shown: the chat falls back to the fixed text, so this measures how often the user gets the
worded one. Threshold: 80 % (8 of 10 in PR3, 12 of 14 since CHAT-10); below it the prompt is changed (and its version bumped) before the PR, or the fixed text stays.
"""

import argparse
import os
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "service"))

import chat  # noqa: E402
import prompts  # noqa: E402
from clarification import ask_count, ask_merchant, ask_period, ask_topic, check_question  # noqa: E402
from ollama_client import OllamaError, OllamaSettings  # noqa: E402

CASES = [
    # The scenario question's six cases left with the question itself (CHAT-10 PR3b D8-B, 12 Nov 2026).
    ask_topic("Bütçemi kısıp tasarruf etsem?"),
    ask_topic("Plan yapalım mı?"),
    ask_topic("Harcamalarımı kontrol altına alsam?"),
    ask_topic("butceme gore ne kadar biriktiririm"),
    # CHAT-10 PR3 (clarify-v3): the questions whose options are now rows under them, worded without listing them.
    ask_period("get_spending_summary", {}, "Ne kadar harcadım?"),
    ask_period("get_category_breakdown", {}, "kategorilere göre harcamam"),
    ask_count("get_monthly_trend", {}, "months", 1, 12, "Harcamam aylara göre nasıl değişti?"),
    ask_merchant("get_merchant_spending", {"period": "Last6Months"}, "şehir", ["Şehir Ulaşım", "Şehirlerarası Otobüs"],
                 "Son 6 ayda şehir'e ne kadar harcadım?"),
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
            text = writer.write(pending.question, pending.kind, [o.label for o in pending.options],
                                time.monotonic() + chat.QUESTION_SECONDS, pending.asked)
            ok = check_question(text, pending)
        except OllamaError as error:
            text, ok = f"{type(error).__name__}: {error}", False
        passed += ok
        shown = " ".join(text.split()).replace("|", "/")
        print(f"| {pending.question} | {'pass' if ok else 'FAIL'} | {time.monotonic() - began:.1f} | {shown} |")
    print(f"\n**{passed}/{len(CASES)}**")
    return 0 if passed >= len(CASES) * 0.8 else 1


if __name__ == "__main__":
    sys.exit(main())
