"""Runs ten date expressions through the AI service's real date interpreter and prints a scored report (CHAT-08 PR3, K3-D1).

Usage (from the repository root, Ollama running, BANKINGAI_LLM_MODEL set; the MCP server is not needed):
    ai/.venv/bin/python ai/model_check/date_eval.py [--model qwen3.5:4b] [--pause 2]

Today is the demo moment, Saturday 10 October 2026. Expected ranges were worked out by hand; two expressions ("bayramda",
"o zamanlar") have no single day or range, so the interpreter must NOT understand them and the chat asks the period instead.
Threshold (K3-D1): below 9 of 10, the interpreter is not used until the user has approved the fallback (D3: "Geçen salı = 6 Ekim
2026 mı?" is asked before anything runs). Run it before and after a change of prompts.DATE_SYSTEM_PROMPT and
bump DATE_PROMPT_VERSION.
"""

import argparse
import os
import sys
import time
from datetime import date
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "service"))

import chat  # noqa: E402
import prompts  # noqa: E402
from dates import CustomerDay  # noqa: E402
from ollama_client import OllamaError, OllamaSettings  # noqa: E402

DAY = CustomerDay(date(2026, 10, 10), "Europe/Istanbul")

# (expression, expected start, expected end); None means "must not be understood".
CASES = [
    ("geçen salı", date(2026, 10, 6), date(2026, 10, 6)),
    ("3 gün önce", date(2026, 10, 7), date(2026, 10, 7)),
    ("evvelsi gün", date(2026, 10, 8), date(2026, 10, 8)),
    ("geçen pazartesi", date(2026, 10, 5), date(2026, 10, 5)),
    ("geçen hafta sonu", date(2026, 10, 3), date(2026, 10, 4)),
    ("geçen yıl", date(2025, 1, 1), date(2025, 12, 31)),
    ("yılbaşından beri", date(2026, 1, 1), date(2026, 10, 10)),
    ("Eylül'ün ilk haftası", date(2026, 9, 1), date(2026, 9, 7)),
    ("bayramda", None, None),
    ("o zamanlar", None, None),
]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--model", help="Ollama model (default: BANKINGAI_LLM_MODEL or qwen3.5:4b)")
    parser.add_argument("--pause", type=float, default=0.0, help="seconds to wait before each case")
    args = parser.parse_args()
    model = args.model or os.environ.get("BANKINGAI_LLM_MODEL") or "qwen3.5:4b"
    settings = OllamaSettings.from_env({**os.environ, "BANKINGAI_LLM_MODEL": model})
    interpreter = chat.LangChainDateInterpreter(settings)

    print(f"## Date eval: {model}, prompt {prompts.DATE_PROMPT_VERSION}, today {DAY.today} (Saturday)\n")
    print("| Expression | Result | Time (s) | Answer |")
    print("|---|---|---|---|")
    passed = 0
    for expression, start, end in CASES:
        time.sleep(args.pause)
        began = time.monotonic()
        try:
            reading = interpreter.interpret(expression, DAY, time.monotonic() + settings.timeout_seconds)
            if reading is None:
                shown, ok = "(not understood)", start is None
            elif reading.kind == "found":
                shown, ok = f"{reading.start} .. {reading.end}", (reading.start, reading.end) == (start, end)
            else:
                shown, ok = f"refused: {reading.reason}", False
        except OllamaError as error:
            shown, ok = f"{type(error).__name__}: {error}", False
        passed += ok
        print(f"| {expression} | {'pass' if ok else 'FAIL'} | {time.monotonic() - began:.1f} | {shown} |")
    print(f"\n**{passed}/{len(CASES)}**")
    return 0 if passed >= 9 else 1


if __name__ == "__main__":
    sys.exit(main())
