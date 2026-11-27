"""Runs date expressions through the chat's own date reading, each several times, and prints a scored report (CHAT-08 PR3, CHAT-14).

Usage (from the repository root, Ollama running, BANKINGAI_LLM_MODEL set; the MCP server is not needed):
    ai/.venv/bin/python ai/model_check/date_eval.py [--model qwen3.5:4b] [--repeat 5] [--pause 0.5]

Today is the demo moment, Saturday 10 October 2026; expected ranges were worked out by hand. Every expression goes the way a chat
message goes (``ChatService._read``: code first, the second step's model only for what code cannot read, then ``fits_words``),
so the measurement is the chat's code, not a copy of it. Each run of a case is right (the expected range), asked (the period is
asked instead) or wrong (another range would have run). CHAT-14 K5-A: the old report asked each expression once and showed 10/10
while the same words gave a week of September for October on the next call; a case passes only if every repeat is right, and the
threshold is no wrong range at all, since a wrong range is a wrong figure and a question costs a tap.
"""

import argparse
import os
import sys
import time
from datetime import date
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "service"))

import chat  # noqa: E402
import prompts  # noqa: E402
from dates import CustomerDay  # noqa: E402
from ollama_client import OllamaError, OllamaSettings  # noqa: E402

DAY = CustomerDay(date(2026, 10, 10), "Europe/Istanbul")

# (expression, expected start, expected end); None means "must be asked": there is no single day or range in the words.
CASES = [
    # Read by code since CHAT-14 (the model never sees them; a repeat cannot differ).
    ("geçen salı", date(2026, 10, 6), date(2026, 10, 6)),
    ("3 gün önce", date(2026, 10, 7), date(2026, 10, 7)),
    ("evvelsi gün", date(2026, 10, 8), date(2026, 10, 8)),
    ("geçen pazartesi", date(2026, 10, 5), date(2026, 10, 5)),
    ("geçen hafta sonu", date(2026, 10, 3), date(2026, 10, 4)),
    ("geçen yıl", date(2025, 1, 1), date(2025, 12, 31)),
    ("yılbaşından beri", date(2026, 1, 1), date(2026, 10, 10)),
    ("Eylül'ün ilk haftası", date(2026, 9, 1), date(2026, 9, 7)),
    ("ekimin ikinci haftası", date(2026, 10, 8), date(2026, 10, 10)),
    ("Ağustosun son haftası", date(2026, 8, 25), date(2026, 8, 31)),
    # Left for the model, its range held to the written words.
    ("salı günü", date(2026, 10, 6), date(2026, 10, 6)),
    ("pazartesi", date(2026, 10, 5), date(2026, 10, 5)),
    ("cuma günü", date(2026, 10, 9), date(2026, 10, 9)),
    ("5/10", date(2026, 10, 5), date(2026, 10, 5)),
    ("bayramda", None, None),
]


def outcome(reading, start, end) -> tuple[str, str]:
    """("right" | "asked" | "wrong", what was read)."""
    if reading.kind == "found":
        shown = f"{reading.start} .. {reading.end}"
        return ("right" if (reading.start, reading.end) == (start, end) else "wrong"), shown
    shown = reading.reason or "(asked)"
    return ("right" if start is None else "asked"), shown


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--model", help="Ollama model (default: BANKINGAI_LLM_MODEL or qwen3.5:4b)")
    parser.add_argument("--repeat", type=int, default=5, help="runs of every case (default 5)")
    parser.add_argument("--pause", type=float, default=0.0, help="seconds to wait before each run")
    args = parser.parse_args()
    model = args.model or os.environ.get("BANKINGAI_LLM_MODEL") or "qwen3.5:4b"
    settings = OllamaSettings.from_env({**os.environ, "BANKINGAI_LLM_MODEL": model})
    # Only what _read uses: the interpreter and the clock.
    service = SimpleNamespace(interpreter=chat.LangChainDateInterpreter(settings), clock=time.monotonic)

    print(f"## Date eval: {model}, prompt {prompts.DATE_PROMPT_VERSION}, today {DAY.today} (Saturday), {args.repeat} runs\n")
    print("| Expression | Right | Asked | Wrong | Read |")
    print("|---|---|---|---|---|")
    totals = {"right": 0, "asked": 0, "wrong": 0}
    passed = 0
    for expression, start, end in CASES:
        counts, seen = {"right": 0, "asked": 0, "wrong": 0}, []
        for _ in range(args.repeat):
            time.sleep(args.pause)
            try:
                reading = chat.ChatService._read(service, expression, DAY, time.monotonic() + settings.timeout_seconds)
                kind, shown = outcome(reading, start, end)
            except OllamaError as error:
                kind, shown = "asked", type(error).__name__
            counts[kind] += 1
            if shown not in seen:
                seen.append(shown)
        for kind, count in counts.items():
            totals[kind] += count
        passed += counts["right"] == args.repeat
        print(f"| {expression} | {counts['right']} | {counts['asked']} | {counts['wrong']} | {'; '.join(seen)} |")
    runs = len(CASES) * args.repeat
    print(f"\n**{passed}/{len(CASES)}** cases right in every run; {totals['right']}/{runs} right, {totals['asked']} asked, "
          f"**{totals['wrong']} wrong**")
    return 0 if totals["wrong"] == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
