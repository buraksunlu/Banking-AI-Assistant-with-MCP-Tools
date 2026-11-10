"""The question asked back when a tool call is missing something, and the user's reply to it (CHAT-03).

K3-A: the question is remembered for one reply, for five minutes, under a digest of the user's token. K4-B: the model may word
the question, but the options are always the backend's closed set or a fixed list, and the reply is matched in code against
the stored options, whatever the model wrote. K5-A: a reply that is not one of them is a new question. MCP-17/18 add the
user's own figures (amount, currency, cut): those questions are always the fixed text, because a worded one could suggest a
figure, and ``later`` holds the figures still to be asked after this one.
"""

import re
import time
from dataclasses import dataclass, replace

from answer_check import FORBIDDEN_MENTIONS, fold_rule, fold_turkish

from formatting import MONTH_NAMES
from grounding import amounts_in, json_number, unclear_amounts, count_in, currency_in, percent_in, period_in
from memory import TokenMemory

# The periods offered when the user named none; any other tool period in the reply counts too (grounding.period_in).
PERIOD_OPTIONS = (("bu ay", "ThisMonth"), ("geçen ay", "LastMonth"), ("son 3 ay", "Last3Months"))
MAX_REPLY_WORDS = 4
# Examples for a count, not numbered: "1" then means one month, not the first option.
MONTH_EXAMPLES = (3, 6, 12)
# The currencies of a budget as people say them; the value is the tool's code.
CURRENCY_OPTIONS = (("TL", "TRY"), ("Euro", "EUR"), ("Dolar", "USD"), ("Sterlin", "GBP"))
# The monthly "what if" saving (INC-03): lira only, so its amount question names the currency.
MONTHLY_SAVING = "what_if_monthly"
# Examples for a cut, not numbered: "20" then means 20 %, not an option.
PERCENT_EXAMPLES = (10, 25, 50)
# CHAT-09 K17-C, K19-C: the two "what if" scenarios and the two planning calculations, as the user is offered them. No spending
# word among the one-off's: "Dün ne harcadım?" after the question is a new question, not the one-off.
CHOICE_KINDS = ("scenario", "topic")
# Folded like the reply they are matched against (K11-A).
_ORDINALS = {fold_rule(word): number for word, number in {
    "birinci": 1, "ilk": 1, "ilki": 1, "ikinci": 2, "ikincisi": 2, "üçüncü": 3, "üçüncüsü": 3, "dördüncü": 4,
    "dördüncüsü": 4, "beşinci": 5, "beşincisi": 5}.items()}


@dataclass(frozen=True)
class Option:
    """One choice: how it is written to the user, and the argument value it stands for."""

    label: str
    value: str | int
    # CHAT-09 K20-B: a choice between two calculations says what each one is, and is recognised by any of its words, so the
    # model may word it the user's way and the user may answer in theirs ("bir kerede", "her ay 2 bin").
    detail: str = ""
    words: re.Pattern | None = None


SCENARIOS = (
    Option("tek seferlik", "what_if_one_off",
           "bir dönemdeki harcamanızla o dönemin başında dolar, euro ya da altın alsaydınız",
           re.compile(fold_rule(r"\btek\s+sefer|\bbir\s+(?:ker|defa|sefer)|\btoplu"))),
    Option("aylık birikim", MONTHLY_SAVING, "son 12 ay boyunca her ay belirli bir TL tutarı biriktirseydiniz",
           re.compile(fold_rule(r"\baylık|\bher\s+ay\b|\bayda\b|\bdüzenli|\bbirikim"))),
)
TOPICS = (
    Option("bütçe kontrolü", "check_budget", "bu ayki harcamanız söylediğiniz bütçeyi aşar mı",
           re.compile(fold_rule(r"bütçe|\blimit|\bsınır|\baş(?:ar|acak|ma)"))),
    Option("tasarruf hesabı", "simulate_savings", "bir harcamayı azaltırsanız ne kadar biriktirirsiniz",
           re.compile(fold_rule(r"tasarruf|\bazalt|\bkıs|biriktir|\bindir|\byarı"))),
)


@dataclass(frozen=True)
class Pending:
    """A tool call waiting for one argument: ``arguments`` holds the rest, ``kind`` is "period", "count", "merchant",
    "amount", "currency" or "percent"; ``later`` names the figures to ask once this one is answered (MCP-17)."""

    tool: str
    arguments: dict
    argument: str
    kind: str
    options: tuple[Option, ...]
    question: str
    asked: str = ""
    minimum: int | None = None
    maximum: int | None = None
    later: tuple[str, ...] = ()
    # CHAT-08: a question with no tool yet has ``tool == ""`` and keeps the user's question without its date words in
    # ``original``; the reply is added to it and the whole is a new question (K12-C). ``note`` says why a date was refused.
    original: str = ""
    note: str = ""
    # INC-03 K15-A: words added after the numbered choices ("or write a month's name").
    hint: str = ""

    @property
    def numbered(self) -> bool:
        """Periods, merchants and currencies are numbered choices; counts, amounts and cuts are examples."""
        return self.kind in ("period", "merchant", "currency", *CHOICE_KINDS)

    @property
    def model_worded(self) -> bool:
        """True when the model may word the question (CHAT-03 K4-B); a figure question is always the fixed text."""
        return self.kind in ("period", "count", "merchant", *CHOICE_KINDS) and not self.note


def ask_period(tool: str, arguments: dict, question: str, note: str = "", original: str = "") -> Pending:
    """The question for a period the user did not name; ``note`` is the reason a date they wrote was refused (CHAT-08 K5-B)."""
    rest = {k: v for k, v in arguments.items() if k != "period"}
    return Pending(tool, rest, "period", "period", tuple(Option(label, value) for label, value in PERIOD_OPTIONS), question,
                   original=original, note=note)


def ask_complete_month(tool: str, arguments: dict, question: str, today=None) -> Pending:
    """The question for a tool that takes one whole, finished calendar month (INC-03 K15-A): last month, named, or another month's name."""
    rest = {k: v for k, v in arguments.items() if k not in ("period", "from", "to")}
    label = "geçen ay" if today is None else f"geçen ay ({MONTH_NAMES[(today.month - 2) % 12]})"
    return Pending(tool, rest, "period", "period", (Option(label, "LastMonth"),), question,
                   note="Bu analiz yalnız tamamlanmış bir ay için yapılır.",
                   hint="Ya da bir ayın adını yazabilirsiniz, örneğin Ağustos.")


def ask_count(tool: str, arguments: dict, argument: str, minimum: int, maximum: int, question: str) -> Pending:
    """The question for a count the user did not write, with examples inside the tool's limits."""
    rest = {k: v for k, v in arguments.items() if k != argument}
    examples = tuple(Option(f"son {n} ay", n) for n in MONTH_EXAMPLES if minimum <= n <= maximum)
    return Pending(tool, rest, argument, "count", examples, question, minimum=minimum, maximum=maximum)


def ask_merchant(tool: str, arguments: dict, asked: str, candidates: list[str], question: str) -> Pending:
    """The question for a merchant name that matched several of the customer's merchants (the backend's candidates)."""
    rest = {k: v for k, v in arguments.items() if k != "merchant"}
    return Pending(tool, rest, "merchant", "merchant", tuple(Option(c, c) for c in candidates), question, asked=asked)


def ask_amount(tool: str, arguments: dict, question: str, later: tuple[str, ...] = ()) -> Pending:
    """The question for a budget amount the user did not write (MCP-17 K3-A)."""
    rest = {k: v for k, v in arguments.items() if k != "amount"}
    return Pending(tool, rest, "amount", "amount", (), question, later=later)


def ask_currency(tool: str, arguments: dict, question: str, later: tuple[str, ...] = ()) -> Pending:
    """The question for a currency the user did not name (MCP-17 K2-A)."""
    rest = {k: v for k, v in arguments.items() if k != "currencyCode"}
    options = tuple(Option(label, value) for label, value in CURRENCY_OPTIONS)
    return Pending(tool, rest, "currencyCode", "currency", options, question, later=later)


def ask_amount_again(pending: Pending, reply: str) -> Pending:
    """The same amount question once more, saying which words were not read as an amount ("5x"; CHAT-10 K9-C)."""
    unclear = " ".join(unclear_amounts(reply)) or reply.strip()
    return replace(pending, note=f"‘{unclear}’ tutarını anlayamadım.")


def ask_percent(tool: str, arguments: dict, question: str, later: tuple[str, ...] = ()) -> Pending:
    """The question for a cut the user did not name (MCP-18 K4-A)."""
    rest = {k: v for k, v in arguments.items() if k != "reductionPercent"}
    examples = tuple(Option(f"%{n}", n) for n in PERCENT_EXAMPLES)
    return Pending(tool, rest, "reductionPercent", "percent", examples, question, minimum=1, maximum=100, later=later)


def ask_scenario(question: str) -> Pending:
    """Which "what if" the user meant (CHAT-09 K17-C): no tool yet, the user's question kept for after the choice."""
    return Pending("", {}, "tool", "scenario", SCENARIOS, question, original=question)


def ask_topic(question: str) -> Pending:
    """Which planning calculation the user meant (CHAT-09 K19-C)."""
    return Pending("", {}, "tool", "topic", TOPICS, question, original=question)


def chosen_tool(pending: Pending, reply: str) -> str | None:
    """The tool a short reply to a choice question names, by number or by its words; None when it names both, neither, or
    the question was no choice (then the reply is a new question, K5-A)."""
    if pending.kind not in CHOICE_KINDS or len(reply.split()) > MAX_REPLY_WORDS:
        return None
    number = _choice_number(reply)
    if number is not None:
        return pending.options[number - 1].value if 1 <= number <= len(pending.options) else None
    folded = fold_turkish(reply)
    named = [o.value for o in pending.options if o.words.search(folded)]
    return named[0] if len(named) == 1 else None


def is_bare_choice(reply: str) -> bool:
    """True for a reply that is only a choice's number ("2", "ikincisi"): it adds no words to the user's question."""
    return _choice_number(reply) is not None


def resolve(pending: Pending, reply: str) -> dict | None:
    """The completed arguments when the reply is one of the choices; None when it is something else (a new question)."""
    # A reply is short ("geçen ay", "son 3 ay olsun", "Şehir Ulaşım"); a longer message that happens to name a period or a
    # merchant ("Bu hafta Starbucks'a ne kadar verdim?") is a question of its own and must not lose its other words.
    if len(reply.split()) > MAX_REPLY_WORDS or pending.kind in CHOICE_KINDS:
        return None
    value = _value(pending, reply)
    if value is None:
        return None
    completed = {**pending.arguments, pending.argument: value}
    # "3000 TL" answers the amount and the currency asked next in one go.
    if pending.kind == "amount" and "currencyCode" in pending.later and (currency := currency_in(reply)) is not None:
        completed["currencyCode"] = currency
    if pending.kind == "amount" and pending.tool == MONTHLY_SAVING:
        # CHAT-09 K6-B: a monthly saving is lira only and its currency is never asked; "100 dolar" keeps its dollar, so the chat
        # refuses it instead of running 100 lira.
        completed["currencyCode"] = currency_in(reply) or "TRY"
    return completed


def template(pending: Pending) -> str:
    """The fixed question: what the user sees when the model's wording is not used."""
    if pending.kind == "count":
        numbers = [str(o.value) for o in pending.options]
        return f"Kaç aylık bakayım? Örneğin son {', '.join(numbers[:-1])} ya da {numbers[-1]} ay."
    note = f"{pending.note} " if pending.note else ""
    if pending.kind == "amount" and pending.tool == MONTHLY_SAVING:
        # CHAT-09 K6-B: the question says the currency, so a bare "5000" answers it whole.
        return f"{note}Ayda kaç TL biriktirirdiniz? Örneğin 1.000."
    if pending.kind == "amount":
        return f"{note}Bütçeniz ne kadar? Örneğin 3.000 TL."
    if pending.kind == "percent":
        labels = [o.label for o in pending.options]
        return f"Ne kadar azaltmayı düşünüyorsunuz? Örneğin {', '.join(labels[:-1])} ya da {labels[-1]}."
    if pending.kind in CHOICE_KINDS:
        both = " ".join(f"{n}) {o.label} ({o.detail})" for n, o in enumerate(pending.options, start=1))
        return f"Bunu iki şekilde hesaplayabilirim: {both}. Hangisini istersiniz?"
    choices = " ".join(f"{n}) {o.label}" for n, o in enumerate(pending.options, start=1))
    if pending.kind == "merchant":
        return f"‘{pending.asked}’ birden fazla işyeriyle eşleşti: {choices}. Hangisini kastettiniz?"
    if pending.kind == "currency":
        return f"Bütçeniz hangi para biriminde? {choices}"
    return f"{pending.note + ' ' if pending.note else ''}Hangi dönem için? {choices}{' ' + pending.hint if pending.hint else ''}"


def check_question(text: str, pending: Pending) -> bool:
    """True when a written question asks, names every option and adds no other number, choice or figure."""
    # It must ask: in the CHAT-03 trial the model sometimes wrote only the list, or a statement without a question.
    if not text.strip() or len(text) > 400 or "?" not in text:
        return False
    folded = fold_turkish(text)
    if any(word in folded for word in FORBIDDEN_MENTIONS):
        return False
    # A count's examples may be written together ("son 3, 6 ya da 12 ay"), so for counts the number itself must be there.
    if pending.kind == "count":
        if any(not re.search(rf"\b{o.value}\b", text) for o in pending.options):
            return False
    elif pending.kind in CHOICE_KINDS:
        # K20-B: an option counts as named by its label or any of its words, so the question may follow the user's phrasing.
        if any(fold_turkish(o.label) not in folded and not o.words.search(folded) for o in pending.options):
            return False
    elif any(fold_turkish(o.label) not in folded for o in pending.options):
        return False
    # Every number must be an option's number, part of an option, or the user's own (a choice or a figure cannot be added).
    allowed = {str(n) for n in range(1, len(pending.options) + 1)} if pending.numbered else set()
    for source in (*(o.label for o in pending.options), *(o.detail for o in pending.options), pending.asked, pending.question):
        allowed.update(re.findall(r"\d+", source))
    return set(re.findall(r"\d+", text)) <= allowed


class PendingStore:
    """At most one waiting question per user, forgotten after ``ttl_seconds`` and used once (memory.TokenMemory)."""

    def __init__(self, ttl_seconds: float = 300.0, capacity: int = 1000, clock=time.monotonic):
        self._memory = TokenMemory(ttl_seconds, capacity, clock)

    def put(self, token: str, pending: Pending) -> None:
        """Remembers the question for this token, replacing an older one."""
        self._memory.put(token, pending)

    def take(self, token: str) -> Pending | None:
        """The waiting question, removed (a reply is used once); None when there is none or it is too old."""
        return self._memory.pop(token)

    def put_back(self, token: str, pending: Pending) -> None:
        """Gives back a question whose reply failed to be answered (CHAT-10 K10-A), unless the turn already asked a new one."""
        if self._memory.get(token) is None:
            self._memory.put(token, pending)


def _value(pending: Pending, reply: str) -> str | int | float | None:
    if pending.kind == "count":
        count = count_in(reply)
        return count if count is not None and pending.minimum <= count <= pending.maximum else None
    if pending.kind == "amount":
        amount = next((a for a in amounts_in(reply) if a > 0), None)
        return None if amount is None else json_number(amount)
    if pending.kind == "percent":
        # A bare "20" is the cut itself: the examples are not numbered.
        cut = percent_in(reply) or count_in(reply)
        return cut if cut is not None and pending.minimum <= cut <= pending.maximum else None
    choice = _choice_number(reply)
    if choice is not None:
        return pending.options[choice - 1].value if 1 <= choice <= len(pending.options) else None
    if pending.kind == "period":
        return period_in(reply)
    if pending.kind == "currency":
        return currency_in(reply)
    return _merchant(pending.options, reply)


def _choice_number(reply: str) -> int | None:
    text = fold_turkish(reply).strip().rstrip(".)")
    if text.isdigit():
        return int(text)
    return _ORDINALS.get(text)


def _merchant(options: tuple[Option, ...], reply: str) -> str | None:
    text = fold_turkish(reply).strip()
    if len(text) < 3:
        return None
    names = {fold_turkish(o.label): o.value for o in options}
    if text in names:
        return names[text]
    # One name inside the reply ("Şehir Ulaşım olsun") or the reply inside one name ("otobüs"); never a choice among several.
    within = [value for name, value in names.items() if name in text] or [value for name, value in names.items() if text in name]
    return within[0] if len(within) == 1 else None
