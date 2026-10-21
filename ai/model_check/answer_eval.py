"""Answer accuracy on the SYN-15 personas: real chat turns, figures checked against independently computed values.

Usage (from the repository root; the AI service, the .NET MCP server with the demo moment, Ollama and .env loaded):
    ai/.venv/bin/python ai/model_check/answer_eval.py

Unlike tool_selection_eval.py (only the model's choice), this sends whole conversations to the AI service's /v1/chat with
a locally signed token for each persona, as the .NET API would forward it, and checks what the user would get: the reply
type, the tool that ran, the facts it was based on and the figures in the answer. Expected figures come from SQL on the
database (answer_cases.json), never from the backend's own analytics code.
"""

import json
import os
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

import jwt  # PyJWT comes with the mcp package

CASES_FILE = Path(__file__).with_name("answer_cases.json")
CUSTOMERS = {"selin": 4, "murat": 5, "zeynep": 6, "hasan": 7, "elif": 8, "can": 9}


def token(customer: str) -> str:
    """A fresh token per case, so no waiting question or last call leaks from one case into the next."""
    now = int(time.time())
    return jwt.encode({"sub": f"10000000-0000-0000-0000-{CUSTOMERS[customer]:012d}", "iss": "BankingAI.Api", "aud": "BankingAI",
                       "iat": now, "nbf": now, "exp": now + 600, "jti": f"{customer}-{time.monotonic_ns()}"},
                      os.environ["BANKINGAI_JWT_SIGNING_KEY"], algorithm="HS256")


def chat(base: str, bearer: str, message: str) -> dict:
    request = urllib.request.Request(f"{base}/v1/chat", json.dumps({"message": message}).encode(),
                                     {"Content-Type": "application/json", "Authorization": f"Bearer {bearer}"})
    with urllib.request.urlopen(request, timeout=45) as response:
        return json.loads(response.read())


def problems(case: dict, reply: dict) -> list[str]:
    """What is wrong with the last reply; empty when the case passes."""
    found = []
    if reply["type"] != case["type"]:
        found.append(f"type {reply['type']}")
    tools = [c["tool"] for c in reply["toolCalls"]]
    if "tool" in case and case["tool"] not in tools:
        found.append(f"tool {tools or 'yok'}")
    facts = " | ".join(f"{f['label']}: {f['text']}" for f in reply["facts"])
    found += [f"fact yok: {text}" for text in case.get("facts", []) if text not in facts]
    found += [f"cevapta yok: {text}" for text in case.get("answer", []) if text not in reply["answer"]]
    found += [f"olmamalı: {text}" for text in case.get("absent", []) if text in facts]
    return found


def main() -> int:
    base = (os.environ.get("BANKINGAI_AI_URL") or "http://127.0.0.1:8000").rstrip("/")
    cases = json.loads(CASES_FILE.read_text(encoding="utf-8"))["cases"]
    print("| Case | Result | Time (s) | Last reply |")
    print("|---|---|---|---|")
    passed = 0
    for case in cases:
        bearer, started = token(case["customer"]), time.monotonic()
        try:
            for message in case["messages"]:
                reply = chat(base, bearer, message)
            issues = problems(case, reply)
            shown = reply["answer"].replace("\n", " ")[:90]
        except urllib.error.HTTPError as error:
            issues, shown = [f"HTTP {error.code}"], error.read().decode()[:90]
        passed += not issues
        print(f"| {case['id']} | {'pass' if not issues else 'FAIL: ' + '; '.join(issues)} | {time.monotonic() - started:.1f} | {shown} |")
    print(f"\n**{passed}/{len(cases)}**")
    return 0 if passed == len(cases) else 1


if __name__ == "__main__":
    sys.exit(main())
