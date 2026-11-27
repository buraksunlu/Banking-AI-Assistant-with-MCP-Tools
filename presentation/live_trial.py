"""Asks every question the slides can send on the live chain as Murat and writes live_trial.json (DEMO-05 PR3, K14-B).

Run it before the presentation and after a question is added to a slide; test_capabilities.py then checks the page against it:

    ai/.venv/bin/python presentation/live_trial.py            # the live services must be up (H on the stage, or run-live.sh)
    ai/.venv/bin/python presentation/live_trial.py --warm     # only the warm-up (scripts/stage.sh runs it after loading the model)

The token is signed locally with the .env key, as ai/model_check/answer_eval.py does; the key is never printed. Only the kind of
reply, the tools and the seconds are kept: no answer text, so no figures of the demo customer land in the repo.
"""

import json
import re
import sys
import time
import urllib.error
import urllib.request
from datetime import date
from pathlib import Path

ROOT = Path(__file__).parent
REPO = ROOT.parent
OUTPUT = ROOT / "live_trial.json"
MURAT = "10000000-0000-0000-0000-000000000005"
CHAT = "http://127.0.0.1:8000/v1/chat"
HOW = ("DEMO-05 PR3 K14-B: every question the slides can send, asked once on the live chain (AI service :8000, MCP, live "
       "database) as Murat with today = date; a follow-up is the whole chain in one conversation. Kind of reply, tools and "
       "seconds only, no figures.")


def page() -> str:
    return (ROOT / "index.html").read_text(encoding="utf-8")


def abilities(html: str) -> dict:
    block = re.search(r'<script type="application/json" id="capabilities">(.*?)</script>', html, re.S)
    return json.loads(block.group(1).replace("<\\/", "</"))


def slide_chains(html: str) -> list[list[str]]:
    """Each slide button with the questions it continues: a "Peki/Ya" button follows the one before it on its slide."""
    chains = []
    for section in re.findall(r'<section class="slide".*?</section>', html, re.S):
        chain: list[str] = []
        for question in re.findall(r'data-q="([^"]+)"', section):
            chain = chain + [question] if question.startswith(("Peki ", "Ya ")) and chain else [question]
            chains.append(chain)
    return chains


def tool_questions(html: str) -> list[tuple[str, str]]:
    return [(tool, question) for tool, _, _, question in re.findall(r'\["(\w+)", (\d), "([^"]+)", "([^"]+)"\]', html)]


def key(steps: list[str]) -> str:
    return " → ".join(steps)


def cases(html: str) -> list[list[str]]:
    """Every question the page can send, once: the abilities as chains, the slide buttons and the tool map."""
    data = abilities(html)
    every = [item["steps"] for item in data["items"] if item["steps"]] + slide_chains(html)
    every += [[question] for _, question in tool_questions(html)]
    seen: set[str] = set()
    return [steps for steps in every if not (key(steps) in seen or seen.add(key(steps)))]


def summary(reply: dict, seconds: float) -> dict:
    return {"type": reply.get("type"), "tools": [call.get("tool") for call in reply.get("toolCalls") or []],
            "seconds": round(seconds, 1)}


def signing_key() -> str:
    for line in (REPO / ".env").read_text().splitlines():
        if line.startswith("BANKINGAI_JWT_SIGNING_KEY="):
            return line.split("=", 1)[1].strip().strip('"').strip("'")
    raise SystemExit("live_trial: no BANKINGAI_JWT_SIGNING_KEY in .env")


def token(signing: str) -> str:
    import jwt  # Only in ai/.venv: the tests import this file with the system Python.

    now = int(time.time())
    claims = {"sub": MURAT, "iss": "BankingAI.Api", "aud": "BankingAI", "iat": now, "nbf": now, "exp": now + 600,
              "jti": f"trial-{time.monotonic_ns()}"}
    return jwt.encode(claims, signing, algorithm="HS256")


def chat(bearer: str, message: str, today: str) -> dict:
    body = json.dumps({"message": message, "today": today, "timeZone": "Europe/Istanbul"}).encode()
    request = urllib.request.Request(CHAT, body, {"Content-Type": "application/json", "Authorization": f"Bearer {bearer}"})
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            return json.loads(response.read())
    except urllib.error.HTTPError as error:
        return {"type": f"http {error.code}"}


# DEMO-05 PR4a K3-A: right after a load the model spent ~25 s on each of the first two questions and on the first follow-up (the
# tool prompt's ~2,250 tokens rebuilt; Ollama reuses its cache only from points it stored); afterwards ~3 s. Each kind twice, in
# words the slides ask, so the audience's first question is not one of them.
WARM = [["Bu ay ne kadar harcadım?", "Peki geçen ay?"],
        ["Geçen ay en çok hangi kategoriye harcadım?", "Peki işyerlerine göre?"]]
TRIES = 3


def warm(ask) -> None:
    """Asks every WARM chain with ``ask`` (a chain → the kind of the last reply); a chain that gets an HTTP error is asked again,
    as the first request to a cold model loads it and may time out."""
    for chain in WARM:
        for _ in range(TRIES):
            if not str(ask(chain)).startswith("http"):
                break
        else:
            raise SystemExit("live_trial: the AI service did not answer the warm-up; is the model up (H → Model)?")


def ask_chain(signing: str, today: str, chain: list[str]) -> str:
    bearer, started, reply = token(signing), time.monotonic(), {}
    for message in chain:  # One token per chain: a follow-up continues the conversation before it.
        reply = chat(bearer, message, today)
    print(f"{time.monotonic() - started:5.1f} s  {reply.get('type')}  {key(chain)}", flush=True)
    return str(reply.get("type"))


def main() -> None:
    signing, today, results = signing_key(), date.today().isoformat(), {}
    warm(lambda chain: ask_chain(signing, today, chain))
    if "--warm" in sys.argv[1:]:
        print("live_trial: warmed")
        return
    for steps in cases(page()):
        bearer, started, reply = token(signing), time.monotonic(), {}
        for message in steps:  # One token per chain: a follow-up continues the conversation before it.
            reply = chat(bearer, message, today)
        results[key(steps)] = summary(reply, time.monotonic() - started)
        print(f"{results[key(steps)]['type']:>13}  {key(steps)}", flush=True)
        time.sleep(0.5)
    trial = {"date": today, "customer": "murat", "database": "bankingai_live", "how": HOW, "results": results}
    OUTPUT.write_text(json.dumps(trial, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    failed = [question for question, result in results.items() if result["type"] not in {"answer", "clarification", "outOfScope"}]
    print(f"live_trial: {len(results)} asked, {len(failed)} failed → {OUTPUT.name}")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
