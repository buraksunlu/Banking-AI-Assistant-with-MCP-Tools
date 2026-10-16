"""Runs the fixed AI-01 cases against local Ollama models and prints a scored report.

Usage (from the repository root):
    python3 ai/model_check/run.py --model qwen3.5:9b --model qwen3.5:4b [--think]

Without --model the name comes from BANKINGAI_LLM_MODEL. Only the standard library is used; the
FastAPI/LangChain service comes later (AI-03/AI-04).
"""

import argparse
import json
import os
import sys
import time
import urllib.request
from pathlib import Path

from scoring import Score, score_explanation, score_structured, score_tool_call

CASES_FILE = Path(__file__).with_name("cases.json")
DEFAULT_OLLAMA_URL = "http://127.0.0.1:11434"
# Generous on purpose: this is a measurement, not the product. Timeout behaviour is AI-02's job.
REQUEST_TIMEOUT_SECONDS = 300


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--model", action="append", help="Ollama model name; repeat to compare several")
    parser.add_argument("--think", action="store_true", help="enable the model's thinking mode")
    parser.add_argument("--only", choices=["tool", "structured", "explanation"], help="run one group only")
    args = parser.parse_args()

    models = args.model or [os.environ.get("BANKINGAI_LLM_MODEL", "")]
    if not models[0]:
        parser.error("give --model or set BANKINGAI_LLM_MODEL")

    # `or`, not a get() default: .env.example leaves the variable empty, which must mean "use the default".
    base_url = (os.environ.get("BANKINGAI_OLLAMA_URL") or DEFAULT_OLLAMA_URL).rstrip("/")
    cases = json.loads(CASES_FILE.read_text(encoding="utf-8"))

    for model in models:
        run_model(base_url, model, args.think, args.only, cases)
    return 0


def run_model(base_url: str, model: str, think: bool, only: str | None, cases: dict) -> None:
    print(f"\n## {model} (think={'on' if think else 'off'})\n")

    # The first request loads the weights; it is timed on its own so it does not inflate the case timings.
    warmup = chat(base_url, {"model": model, "messages": [{"role": "user", "content": "Merhaba"}]}, think)
    print(f"Load: {warmup['load_duration'] / 1e9:.1f} s\n")
    print("| Case | Result | Time (s) | Tokens/s | Detail |")
    print("|---|---|---|---|---|")

    groups = {
        "tool": lambda: run_tool_cases(base_url, model, think, cases),
        "structured": lambda: run_structured_cases(base_url, model, think, cases),
        "explanation": lambda: run_explanation_cases(base_url, model, think, cases),
    }
    totals = {name: group() for name, group in groups.items() if only in (None, name)}

    print()
    for name, (passed, count, seconds) in totals.items():
        print(f"- {name}: {passed}/{count} passed, average {seconds / count:.1f} s")
    print(f"- memory: {loaded_size(base_url, model)}")


def run_tool_cases(base_url: str, model: str, think: bool, cases: dict) -> tuple[int, int, float]:
    results = []
    for case in cases["tool_cases"]:
        response = chat(base_url, {
            "model": model,
            "messages": [{"role": "system", "content": cases["tool_system_prompt"]},
                         {"role": "user", "content": case["question"]}],
            "tools": cases["tools"],
        }, think)
        score = score_tool_call(case["expected"], response["message"].get("tool_calls"))
        results.append(report(case["id"], score, response))
    return summarize(results)


def run_structured_cases(base_url: str, model: str, think: bool, cases: dict) -> tuple[int, int, float]:
    results = []
    for case in cases["structured_cases"]:
        response = chat(base_url, {
            "model": model,
            "messages": [{"role": "system", "content": cases["structured_system_prompt"]},
                         {"role": "user", "content": case["question"]}],
            "format": cases["structured_schema"],
        }, think)
        score = score_structured(case["expected"], response["message"].get("content", ""))
        results.append(report(case["id"], score, response))
    return summarize(results)


def run_explanation_cases(base_url: str, model: str, think: bool, cases: dict) -> tuple[int, int, float]:
    results, answers = [], []
    for case in cases["explanation_cases"]:
        response = chat(base_url, {
            "model": model,
            "messages": [{"role": "system", "content": cases["explanation_system_prompt"]},
                         {"role": "user", "content": f"Soru: {case['question']}\nVeri: {case['data']}"}],
        }, think)
        content = response["message"].get("content", "")
        score = score_explanation([case["data"], case["question"]], case["must_include"], content)
        results.append(report(case["id"], score, response))
        answers.append(f"- {case['id']}: {' '.join(content.split())}")
    # The texts are printed after the table so a human can judge the Turkish, which no score measures.
    print("\n" + "\n".join(answers))
    return summarize(results)


def chat(base_url: str, body: dict, think: bool) -> dict:
    # Without thinking, temperature 0 so the same question gets the same choice. With thinking, greedy decoding
    # loops (measured: 1500 tokens of thinking for "Merhaba", no answer), so Qwen's recommended sampling is used.
    options = {"temperature": 0.6, "top_p": 0.95, "top_k": 20} if think else {"temperature": 0}
    body = {**body, "stream": False, "think": think, "options": options}
    request = urllib.request.Request(f"{base_url}/api/chat", data=json.dumps(body).encode("utf-8"),
                                     headers={"Content-Type": "application/json"})
    started = time.monotonic()
    with urllib.request.urlopen(request, timeout=REQUEST_TIMEOUT_SECONDS) as response:
        result = json.loads(response.read())
    result["wall_seconds"] = time.monotonic() - started
    return result


def report(case_id: str, score: Score, response: dict) -> tuple[bool, float]:
    seconds = response["wall_seconds"]
    eval_seconds = response.get("eval_duration", 0) / 1e9
    tokens_per_second = response.get("eval_count", 0) / eval_seconds if eval_seconds else 0
    detail = score.detail.replace("|", "/")
    print(f"| {case_id} | {'ok' if score.passed else 'FAIL'} | {seconds:.1f} | {tokens_per_second:.0f} | {detail} |")
    return score.passed, seconds


def summarize(results: list[tuple[bool, float]]) -> tuple[int, int, float]:
    return sum(passed for passed, _ in results), len(results), sum(seconds for _, seconds in results)


def loaded_size(base_url: str, model: str) -> str:
    with urllib.request.urlopen(f"{base_url}/api/ps", timeout=10) as response:
        running = json.loads(response.read()).get("models", [])
    sizes = [m["size"] for m in running if m.get("name") == model or m.get("model") == model]
    return f"{sizes[0] / 1024 ** 3:.1f} GB loaded" if sizes else "unknown"


if __name__ == "__main__":
    sys.exit(main())
