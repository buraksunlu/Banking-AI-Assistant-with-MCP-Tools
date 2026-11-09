"""Many wordings of every question the chat answers, run through the chat's own code (CHAT-09 PR1).

The 91 tool-selection cases were written by hand, so every rule was tested with the words it was written from: "Geçen ayki
harcamalarımı biriktirseydim nolurdu" reached the wrong scenario in the simulator although every case passed. This module
expands ``phrasing_templates.json`` (intents with slots, real phrasings, short dialogs) into hundreds of phrasings and the ways
people type them (K2-C, K3-C), and runs each through the real ``ChatService``:

    phrasing → ChatService.answer (routing, code rules, dates, grounding, questions back) → the MCP call it would make

Nothing is copied from the chat: the gateway stops at the call (``Captured``), and the model is replaced either by an oracle that
always chooses right (the model-free test: "does the code let the right answer through?") or by the real selector
(``phrasing_eval.py``: "does the model choose right?"). K1-B, K4-B. Standard library and the service modules, deterministic.
"""

import hashlib
import itertools
import json
import random
import re
import sys
from dataclasses import dataclass
from datetime import date
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "service"))

from chat import DATE_UNSUPPORTED_TEXT, ChatReply, ChatService, ToolCall  # noqa: E402
from conversation import LastCall  # noqa: E402
from dates import CustomerDay  # noqa: E402
from ollama_client import OllamaSettings  # noqa: E402
from prompts import ADVICE_DECLINED_TEXT, CLARIFY_TEXT, LIRA_ONLY_TEXT, OUT_OF_SCOPE_TEXT  # noqa: E402
from routing import DATE_FIELDS, Router  # noqa: E402

HERE = Path(__file__).resolve().parent
TEMPLATES_FILE = HERE / "phrasing_templates.json"
GAPS_FILE = HERE / "phrasing_gaps.json"
# The tools as the MCP server publishes them, kept so the model-free test needs no server (phrasing_eval.py --refresh-tools).
TOOLS_FILE = HERE / "mcp_tools.json"

# The demo moment of the seed data (BANKINGAI_DEMO_NOW): last month is September 2026.
DAY = CustomerDay(date(2026, 10, 10), "Europe/Istanbul")
TOKEN = "phrasing-probe"
# The probe never waits for a model; the oracle answers at once.
SETTINGS = OllamaSettings("http://127.0.0.1:11434", "probe", 30.0)

# K3-C: the gating transformations keep the meaning; a typo is reported only.
TRANSFORMS = ("plain", "ascii", "upper", "lower", "colloquial")
_ASCII = str.maketrans("çğıöşüÇĞİÖŞÜ", "cgiosuCGIOSU")
_COLLOQUIAL = (("ne olurdu", "nolurdu"), ("Ne olurdu", "Nolurdu"), ("ne yaptım", "naptım"), ("Ne yaptım", "Naptım"),
               ("ne yapardım", "napardım"))
# A typo is made in a word at least this long: short words ("ay", "dün") lose their meaning with one letter less.
_TYPO_MIN = 6


@dataclass(frozen=True)
class Variant:
    """One phrasing: its stable ``id`` (intent.template.slots.transform), what the chat should end in (``expected``: a call, a
    list of equally right calls, or a kind such as "clarify"), what the model writes in the oracle's place when that differs
    (``oracle``), the previous call of a follow-up (``context``) and the user's messages (``turns``)."""

    id: str
    intent: str
    template: str
    text: str
    expected: object
    oracle: object
    transform: str
    gating: bool
    context: dict | None
    turns: tuple[str, ...]


@dataclass(frozen=True)
class Outcome:
    """How a turn ended: "call" (with the tool and arguments that reached the server) or the kind of fixed reply."""

    kind: str
    tool: str | None = None
    arguments: dict | None = None


class Captured(Exception):
    """Raised by the probe gateway in place of running a tool: the call is the outcome of the turn."""

    def __init__(self, call: ToolCall):
        super().__init__(call.tool)
        self.call = call


def turkish_upper(text: str) -> str:
    """Upper case as a Turkish keyboard writes it (i → İ, ı → I)."""
    return text.replace("i", "İ").replace("ı", "I").upper()


def turkish_lower(text: str) -> str:
    """Lower case as a Turkish keyboard writes it (İ → i, I → ı)."""
    return text.replace("I", "ı").replace("İ", "i").lower()


def transform(name: str, text: str, seed: str) -> str:
    """The text as people type it: without Turkish letters, in capitals, in lower case without the question mark, in spoken
    form, or with one letter missing (``seed`` picks the letter)."""
    if name == "ascii":
        return text.translate(_ASCII)
    if name == "upper":
        return turkish_upper(text)
    if name == "lower":
        return turkish_lower(text).rstrip("?!. ")
    if name == "colloquial":
        for written, spoken in _COLLOQUIAL:
            text = text.replace(written, spoken)
        return text
    if name == "typo":
        return _typo(text, seed)
    return text


def _typo(text: str, seed: str) -> str:
    words = text.split(" ")
    long = [i for i, word in enumerate(words) if len(word) >= _TYPO_MIN]
    if not long:
        return text
    # hashlib, not hash(): Python salts str hashes per process, and the same typo must come back on every run.
    digest = int(hashlib.sha256(seed.encode()).hexdigest(), 16)
    index = long[digest % len(long)]
    word = words[index]
    position = 1 + (digest // len(long)) % (len(word) - 1)
    words[index] = word[:position] + word[position + 1:]
    return " ".join(words)


def load(path: Path = TEMPLATES_FILE) -> dict:
    """The templates, real phrasings and dialogs."""
    return json.loads(path.read_text(encoding="utf-8"))


def load_tools(path: Path = TOOLS_FILE) -> list[dict]:
    """The snapshot of the published tools, in the model's tool shape."""
    return json.loads(path.read_text(encoding="utf-8"))["tools"]


def expand(data: dict, typos: bool = False) -> list[Variant]:
    """Every phrasing of the templates (each slot combination), of the real list and of the dialogs, in every transformation
    that changes its text; typos only when asked (report-only, K3-C)."""
    variants: list[Variant] = []
    for intent in data.get("intents", []):
        for template in intent["templates"]:
            slots = template.get("slots", intent.get("slots", {}))
            used = [name for name in re.findall(r"{(\w+)}", template["text"]) if name in slots]
            for choice in itertools.product(*(list(enumerate(slots[name])) for name in used)):
                text, arguments, suffix = template["text"], dict(template.get("args", {})), ""
                for name, (index, option) in zip(used, choice):
                    text = text.replace("{" + name + "}", option["text"])
                    arguments.update(option.get("args", {}))
                    suffix += f".{name}{index}"
                base = f"{intent['id']}.{template['id']}{suffix}"
                variants += _transformed(base, intent["id"], template, (_capital(text),), _with(template["expected"], arguments), typos)
    for item in data.get("real", []):
        variants += _transformed(f"real.{item['id']}", "real", item, (item["text"],), item["expected"], typos)
    for item in data.get("dialogs", []):
        variants += _transformed(f"dialog.{item['id']}", "dialog", item, tuple(item["turns"]), item["expected"], typos)
    return variants


def _transformed(base: str, intent: str, item: dict, turns: tuple[str, ...], expected, typos: bool) -> list[Variant]:
    names = TRANSFORMS + (("typo",) if typos else ())
    seen, result = set(), []
    for name in names:
        if name == "typo":
            written = (transform("typo", turns[0], base), *turns[1:])
        else:
            written = tuple(transform(name, turn, base) for turn in turns)
        if written in seen:
            continue
        seen.add(written)
        result.append(Variant(f"{base}.{name}", intent, item["id"], written[0], expected, item.get("oracle"), name, name != "typo",
                              item.get("context"), written))
    return result


def _capital(text: str) -> str:
    return turkish_upper(text[0]) + text[1:] if text else text


def _with(expected, arguments: dict):
    """The expected call(s) with the template's and the slots' arguments added; a kind ("clarify") stays as it is."""
    if isinstance(expected, list):
        return [_with(choice, arguments) for choice in expected]
    if isinstance(expected, dict):
        return {"tool": expected["tool"], "arguments": {**expected["arguments"], **arguments}}
    return expected


def sample(variants: list[Variant], per_intent: int, seed: int, gating: bool = True) -> list[Variant]:
    """At most ``per_intent`` phrasings of each intent (the gating ones, or with ``gating=False`` the report-only typos), the
    same ones for the same seed, in the variants' own order."""
    chosen: set[str] = set()
    rng = random.Random(seed)
    for intent in dict.fromkeys(v.intent for v in variants):
        pool = [v.id for v in variants if v.intent == intent and v.gating is gating]
        rng.shuffle(pool)
        chosen.update(pool[:per_intent])
    return [v for v in variants if v.id in chosen]


def outcome_of(reply, asked: str | None = None) -> Outcome:
    """The outcome of a turn: the call the gateway caught, or the kind of reply the user sees; a question asked back is
    "clarify:<what it asks>" when the waiting question's kind is known (``asked``)."""
    if isinstance(reply, Captured):
        return Outcome("call", reply.call.tool, dict(reply.call.arguments))
    assert isinstance(reply, ChatReply)
    if reply.type == "clarification":
        if reply.answer == CLARIFY_TEXT:
            return Outcome("unclear")
        return Outcome(f"clarify:{asked}" if asked else "clarify")
    if reply.type == "outOfScope":
        kinds = {ADVICE_DECLINED_TEXT: "advice", DATE_UNSUPPORTED_TEXT: "date_unsupported", OUT_OF_SCOPE_TEXT: "out_of_scope"}
        return Outcome(kinds.get(reply.answer, "out_of_scope"))
    return Outcome("lira_only" if reply.answer == LIRA_ONLY_TEXT else "answer")


def matches(expected, outcome: Outcome) -> bool:
    """True when the outcome is the expected kind, or exactly one of the expected calls (merchant names without case; an
    argument written as null counts as left out)."""
    if isinstance(expected, list):
        return any(matches(choice, outcome) for choice in expected)
    if isinstance(expected, str):
        # "clarify" accepts any question back; "clarify:amount" only that one.
        return outcome.kind == expected or (expected == "clarify" and outcome.kind.startswith("clarify:"))
    if outcome.kind != "call" or outcome.tool != expected["tool"]:
        return False
    return _folded(outcome.arguments or {}) == _folded(expected["arguments"])


def _folded(arguments: dict) -> dict:
    return {k: v.casefold() if k == "merchant" and isinstance(v, str) else v for k, v in arguments.items() if v is not None}


class OracleSelector:
    """The model choosing right: the expected call when the route showed its tool, nothing otherwise; only on the first turn
    (a reply to a question asked back is completed by code, never selected)."""

    def __init__(self, expected):
        self.expected, self.used = expected, False

    def select(self, message, tools, deadline, context=None) -> list[ToolCall]:
        if self.used:
            return []
        self.used = True
        call = self.expected[0] if isinstance(self.expected, list) else self.expected
        if not isinstance(call, dict):
            return []
        shown = {tool["function"]["name"]: tool for tool in tools}
        if call["tool"] not in shown:
            return []
        arguments = {k: v for k, v in call["arguments"].items() if k not in DATE_FIELDS}
        if arguments.get("period") == "Custom":
            # The model is shown "bu ay" in place of the date (CHAT-08), and a complete-month tool has no "this month" to write.
            enum = shown[call["tool"]]["function"].get("parameters", {}).get("properties", {}).get("period", {}).get("enum", [])
            arguments["period"] = "ThisMonth" if "ThisMonth" in enum else "LastMonth"
        return [ToolCall(call["tool"], arguments)]


class ProbeGateway:
    """Lists the given tools and stops the turn where the chat would call one."""

    def __init__(self, tools: list[dict]):
        self.tools, self.listed = tools, 0

    def list_tools(self, token, deadline):
        self.listed += 1
        return self.tools

    def call(self, tool, arguments, token, deadline):
        raise Captured(ToolCall(tool, dict(arguments)))


class _NoExplainer:
    def explain(self, question, facts, deadline=None):
        raise AssertionError("the probe stops before any explanation")


def probe(selector, tools: list[dict], router: Router | None = None, interpreter=None) -> tuple[ChatService, ProbeGateway]:
    """The real chat service with ``selector`` in the model's place and a gateway that stops at the call."""
    gateway = ProbeGateway(tools)
    return ChatService(selector, gateway, _NoExplainer(), SETTINGS, router=router, interpreter=interpreter), gateway


def run_variant(variant: Variant, selector=None, tools: list[dict] | None = None, router: Router | None = None,
                interpreter=None) -> Outcome:
    """Sends the variant's messages through the chat and returns how the last one ended (or the first call that ran)."""
    chat, _ = probe(selector or OracleSelector(variant.oracle if variant.oracle is not None else variant.expected),
                    tools if tools is not None else load_tools(), router, interpreter)
    if variant.context:
        given = variant.context
        chat.history.remember(TOKEN, LastCall(given["tool"], given["arguments"], given["question"]))
    reply = None
    for turn in variant.turns:
        try:
            reply = chat.answer(turn, TOKEN, DAY)
        except Captured as caught:
            return outcome_of(caught)
    # The question the last turn left waiting says what was asked (amount, currency, period…); the probe is thrown away after.
    waiting = chat.waiting.take(TOKEN)
    return outcome_of(reply, waiting.kind if waiting is not None else None)


def failures(variants: list[Variant]) -> list[tuple[Variant, Outcome]]:
    """The variants the model-free run does not end as expected, with what happened instead."""
    result = []
    for variant in variants:
        outcome = run_variant(variant)
        if not matches(variant.expected, outcome):
            result.append((variant, outcome))
    return result


def main() -> int:
    """Prints the model-free failures (``--typos`` adds the report-only typos): the list a gap file is written from."""
    variants = expand(load(), typos="--typos" in sys.argv)
    failed = failures(variants)
    gating = [v for v in variants if v.gating]
    print(f"{len(gating)} gating phrasings, {len(variants) - len(gating)} report-only; "
          f"{sum(v.gating for v, _ in failed)} gating failures, {sum(not v.gating for v, _ in failed)} report-only failures\n")
    for variant, outcome in failed:
        got = f"{outcome.tool}({json.dumps(outcome.arguments, ensure_ascii=False)})" if outcome.kind == "call" else outcome.kind
        print(f"{variant.id}\t{' / '.join(variant.turns)}\t→ {got}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
