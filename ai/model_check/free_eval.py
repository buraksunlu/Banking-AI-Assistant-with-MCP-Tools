"""Free phrasings: does the chat answer what people really type, or answer something else with confidence (CHAT-12 PR1)?

    ai/.venv/bin/python ai/model_check/free_eval.py                 # model-free: where the code sends each sentence
    ai/.venv/bin/python ai/model_check/free_eval.py --model --pause 1   # the real router, intent question and selector (Ollama)
    ai/.venv/bin/python ai/model_check/free_eval.py --shuffle       # report only: the measured phrasings in another word order
    ai/.venv/bin/python ai/model_check/free_eval.py --write-baseline   # after a deliberate change: write the model-free buckets

CHAT-09 counted right and wrong only. Here every turn ends in one of four buckets (K8-A): right (the expected call or reply), asked
(a question back: safe, one tap more), unanswered (the out-of-scope text for a question it could answer: safe, unhelpful) and wrong
(another tool's figures: dangerous). The model-free run uses phrasings.OracleSelector, which chooses nothing when the right tool is
not on the list; a real model would choose from that wrong list, so there the turn counts as wrong (a wrong door). Each sentence
runs through the real ChatService (phrasings.probe); nothing of the chat is copied. Standard library and the service modules.
"""

import argparse
import json
import os
import random
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "service"))

from phrasings import (DAY, TOKEN, Captured, OracleSelector, Outcome, expand, load, load_tools, matches, outcome_of,  # noqa: E402
                       probe)

HERE = Path(__file__).resolve().parent
FREE_FILE = HERE / "free_phrasings.json"
BASELINE_FILE = HERE / "free_baseline.json"
RIGHT, ASKED, UNANSWERED, WRONG = "right", "asked", "unanswered", "wrong"
BUCKETS = (RIGHT, ASKED, UNANSWERED, WRONG)
LABELS = {RIGHT: "doğru", ASKED: "sordu", UNANSWERED: "cevapsız", WRONG: "yanlış"}


class RecordingSelector:
    """A selector that remembers the tools it was shown on each call (the door the router opened)."""

    def __init__(self, inner):
        self.inner, self.shown = inner, []

    def select(self, message, tools, deadline, context=None):
        self.shown.append([tool["function"]["name"] for tool in tools])
        return self.inner.select(message, tools, deadline, context)


def all_cases(data: dict) -> list[dict]:
    """The written cases and the user's own sentences (K2-D), in one list."""
    return [*data["cases"], *data.get("burak", [])]


def _calls(expected) -> list[dict]:
    choices = expected if isinstance(expected, list) else [expected]
    return [choice for choice in choices if isinstance(choice, dict)]


def bucket(expected, outcome: Outcome, shown: list[list[str]], modelless: bool) -> str:
    """The bucket of one turn. ``shown``: the tools of each selection; ``modelless``: the oracle stood in for the model."""
    if matches(expected, outcome):
        return RIGHT
    if outcome.kind == "call":
        return WRONG
    calls = _calls(expected)
    if calls and modelless and shown and not any(call["tool"] in names for call in calls for names in shown):
        # The oracle chose nothing because the right tool was not there; a real model would have chosen from what was.
        return WRONG
    if outcome.kind == "unclear" or outcome.kind.startswith("clarify"):
        return ASKED
    return UNANSWERED


def run_case(text: str, expected, selector=None, router=None, interpreter=None, tools=None) -> tuple[Outcome, list[list[str]]]:
    """One sentence through the real chat service; the outcome and the tools the model was shown."""
    recorder = RecordingSelector(selector or OracleSelector(expected))
    chat, _ = probe(recorder, tools if tools is not None else load_tools(), router, interpreter)
    try:
        reply = chat.answer(text, TOKEN, DAY)
    except Captured as caught:
        return outcome_of(caught), recorder.shown
    waiting = chat.waiting.take(TOKEN)
    return outcome_of(reply, waiting.kind if waiting is not None else None), recorder.shown


def summary(buckets: list[str]) -> dict:
    """How many turns ended in each bucket, and the share of wrong ones in percent (one decimal)."""
    counts = {name: buckets.count(name) for name in BUCKETS}
    total = len(buckets)
    return {**counts, "total": total, "wrongShare": round(100 * counts[WRONG] / total, 1) if total else 0.0}


def shuffled(items: list[tuple[str, str, object]], seed: int) -> list[tuple[str, str, object]]:
    """The same sentences with their words in another order (the same order each run); a closing mark stays at the end."""
    rng = random.Random(seed)
    result = []
    for identifier, text, expected in items:
        stripped = text.rstrip("?!.")
        words = stripped.split()
        rng.shuffle(words)
        result.append((identifier, " ".join(words) + text[len(stripped):], expected))
    return result


def _measured_phrasings() -> list[tuple[str, str, object]]:
    # The single-turn, plain phrasings of the CHAT-09 set: the ones whose word order can be changed and still mean the same.
    return [(v.id, v.text, v.expected) for v in expand(load()) if v.transform == "plain" and len(v.turns) == 1 and not v.context]


def _model_parts():
    import chat as chat_module
    from ollama_client import OllamaSettings
    from routing import Router
    settings = OllamaSettings.from_env({**os.environ, "BANKINGAI_LLM_MODEL": os.environ.get("BANKINGAI_LLM_MODEL") or "qwen3.5:4b"})
    return (chat_module.LangChainToolSelector(settings), Router(chat_module.LangChainIntentClassifier(settings)),
            chat_module.LangChainDateInterpreter(settings))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", action="store_true", help="the real router, intent question and selector (Ollama)")
    parser.add_argument("--shuffle", action="store_true", help="report only: the measured phrasings in another word order")
    parser.add_argument("--write-baseline", action="store_true", help="write the model-free buckets to free_baseline.json")
    parser.add_argument("--pause", type=float, default=0.0, help="seconds to wait before each model case")
    args = parser.parse_args()

    if args.shuffle:
        items = shuffled(_measured_phrasings(), seed=12)
        results = [(i, t, bucket(e, *run_case(t, e), modelless=True)) for i, t, e in items]
        counts = summary([b for _, _, b in results])
        print(f"## Word order shuffled (model-free, report only): {counts}\n")
        for identifier, text, result in results:
            if result == WRONG:
                print(f"{identifier}\t{text}")
        return 0

    data = json.loads(FREE_FILE.read_text(encoding="utf-8"))
    selector = router = interpreter = None
    if args.model:
        selector, router, interpreter = _model_parts()
    print(f"## Free phrasings: {'qwen model' if args.model else 'model-free'}\n")
    print("| Case | Kind | Bucket | Time (s) | Text | Outcome |")
    print("|---|---|---|---|---|---|")
    buckets = {}
    for case in all_cases(data):
        if args.model:
            time.sleep(args.pause)
        started = time.monotonic()
        outcome, shown = run_case(case["text"], case["expected"], selector=selector, router=router, interpreter=interpreter)
        buckets[case["id"]] = result = bucket(case["expected"], outcome, shown, modelless=not args.model)
        got = f"{outcome.tool}({json.dumps(outcome.arguments, ensure_ascii=False)})" if outcome.kind == "call" else outcome.kind
        print(f"| {case['id']} | {case.get('kind', 'burak')} | {LABELS[result]} | {time.monotonic() - started:.1f} | {case['text']} | {got} |")
    counts = summary(list(buckets.values()))
    print(f"\n**{' · '.join(f'{LABELS[b]} {counts[b]}' for b in BUCKETS)}** of {counts['total']}; wrong {counts['wrongShare']} %")
    if args.write_baseline:
        if args.model:
            print("The baseline is the model-free run; not written.")
            return 2
        BASELINE_FILE.write_text(json.dumps({"description": "CHAT-12: where each free phrasing ends without a model (free_eval.py "
                                                           "--write-baseline); a change shows in the PR.", "buckets": buckets},
                                            ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
