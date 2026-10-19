"""Runs the fixed explanation cases through the AI service's real explainer and prints a scored report (AI-05).

Usage (from the repository root, Ollama running):
    ai/.venv/bin/python ai/model_check/explanation_eval.py [--model qwen3.5:4b]

It measures the prompt that is in ai/service right now, so running it before and after a prompt change gives the
comparison. Every score is computed by code (scoring.py); the answers are printed for a human to read the Turkish.
"""

import argparse
import json
import os
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "service"))

import explainer as explainer_module  # noqa: E402
import prompts  # noqa: E402
from contracts import Fact  # noqa: E402
from ollama_client import OllamaError, OllamaSettings  # noqa: E402
from scoring import score_explanation, score_text_rules  # noqa: E402

CASES_FILE = Path(__file__).with_name("explanation_cases.json")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--model", help="Ollama model (default: BANKINGAI_LLM_MODEL or qwen3.5:4b)")
    args = parser.parse_args()

    model = args.model or os.environ.get("BANKINGAI_LLM_MODEL") or "qwen3.5:4b"
    settings = OllamaSettings.from_env({**os.environ, "BANKINGAI_LLM_MODEL": model})
    explainer = explainer_module.LangChainExplainer(settings)
    data = json.loads(CASES_FILE.read_text(encoding="utf-8"))
    version = prompts.PROMPT_VERSION

    print(f"## Explanation eval: {model}, prompt {version}\n")
    print("| Case | Result | Time (s) | Detail |")
    print("|---|---|---|---|")

    answers, passed = [], 0
    for case in data["cases"]:
        facts = [Fact(label=label, text=text) for label, text in case["facts"]]
        started = time.monotonic()
        try:
            answer = explainer.explain(case["question"], facts)
        except OllamaError as error:
            answer, problems = "", [f"{type(error).__name__}: {error}"]
        else:
            # Numbers may come from the facts or the question; the question can hold one too ("Son 6 ayda").
            sources = [case["question"], *(f"{label} {text}" for label, text in case["facts"])]
            scores = [
                score_explanation(sources, case["must_include"], answer),
                score_text_rules(answer, case["must_include_any"], case["must_not_include"] + data["must_not_include_always"],
                                 data["max_sentences"]),
            ]
            problems = [s.detail for s in scores if not s.passed]
        seconds = time.monotonic() - started
        passed += not problems
        detail = "; ".join(problems).replace("|", "/")
        print(f"| {case['id']} {case['name']} | {'ok' if not problems else 'FAIL'} | {seconds:.1f} | {detail} |")
        answers.append(f"- {case['id']}: {' '.join(answer.split())}")

    print(f"\n**{passed}/{len(data['cases'])} passed**\n")
    print("\n".join(answers))
    return 0


if __name__ == "__main__":
    sys.exit(main())
