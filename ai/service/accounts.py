"""The everyday banking questions (ACC-01b): balances, a purchase search with an amount limit, income and transfers.

ACC-01a put four tools on the MCP server; this module holds the word rules that reach them and the arguments code reads itself.
Decisions: K2-C (the words choose the tool, the model fills in the period; when it chooses nothing, code reads the call from the
words), K3-A (the amount limit and its side are code's: "2.000 TL üstü" is a minimum, the model's figure is never used), K6-C (a
"yattı mı / geldi mi" question without a period is about this month; any other missing period is asked). The direction of a
transfer is read the same way as the limit. Every rule is written in Turkish and folded (CHAT-09 K11-A).
"""

import re
from dataclasses import dataclass
from decimal import Decimal

from answer_check import fold_rule, fold_turkish
from spelling import WHAT_IF_ENDING
from grounding import amounts_in, currency_in, json_number, mentions_period, period_in

BALANCE, SEARCH, INCOME, TRANSFERS = "get_accounts", "search_transactions", "get_income", "get_transfers"
ACCOUNT_TOOLS = (BALANCE, SEARCH, INCOME, TRANSFERS)
LIRA = "TRY"
LIMIT_FIELDS = ("minAmount", "maxAmount", "currencyCode")

# Spending, a payment or a transfer next to "hesabım" is about what happened on the account, not its balance; so is what is left
# of a budget ("bütçemden kalan para").
_NOT_BALANCE = re.compile(fold_rule(r"harca|ödeme|\bödedi|işlem|gönder|transfer|havale|maaş|gelir|bütçe"))
_BALANCE = re.compile(fold_rule(
    r"bakiye|\bhesab(?:ım|ımda|ımdaki)\b|\bhesaplar(?:ım|ımda|ımdaki|ımı)\b|\bne\s+kadar\s+param\b"
    r"|\bparam\s+(?:var|kaldı|ne\s+kadar)"
    # CHAT-12 PR3 K1-C: phrasings only the model's intent question placed (N01, N02, N07, N08); a cold model past its 5 s sent
    # them to the spending tools.
    r"|\b(?:elimde|kartımda|kartta|hesapta)\s+ne\s+kadar\s+(?:param?\s+)?(?:var|kaldı)|\bkalan\s+param?\b|\bhesap\s+durum"))
# INC-03 keeps the income share and the what-ifs: a salary beside spending, a share or a saving is theirs, not this list's.
# CHAT-12 PR3 K1-C: "kazancım", "ne kadar kazandım" and money that came in (N14, N18); "para yatırdım" (deposited) is none.
_INCOME = re.compile(fold_rule(r"\b(?:maaş|burs|gelir|emekli\s+aylığ)|\bkazanc|\bkazandı[mğ]|\bpara\s+yat(?:tı|mış|an)\b"))
_NOT_INCOME = re.compile(fold_rule(
    rf"harca|\bpay|yüzde|\boran|göre|tasarruf|öneri|biriktir|{WHAT_IF_ENDING}|olarak|cinsinden|bütçe|\bgitti|kalan"))
# Only what was sent or received; a request to send ("para gönder", "transfer yap") is the router's out-of-scope rule.
# CHAT-12 PR3 K2-A: "kimlere/kimlerden" and the spoken "para attım / atan" (N19); "para at" as a request is the router's.
_TRANSFER = re.compile(fold_rule(
    r"\btransfer|\bhavale|\beft\b|\bgönderd|\bgönderil|\byollad|\bkimden\b|\bkime\b|\bpara\s+gel|\b(?:gelen|giden)\s+(?:para|transfer)"
    r"|\bkimler(?:e|den)\b|\bpara\s+at(?:tı|mış|an\b)"))
_SPENDING = re.compile(fold_rule(r"harca|alışveriş|işlem|ödeme|masraf"))

# An amount as people write it ("2.000", "5 bin", "2k", "bin"), its currency and the ending glued to it ("TL'den", "liranın").
_AMOUNT = r"(?:\d[\d.,]*(?:\s*(?:bin|milyon)|k)?|bin)(?:\s*(?:tl|try|lira|₺|euro|eur|avro|dolar|usd|\$|sterlin|gbp))?(?:'?[a-z]+)?"
_MINIMUM_SIDE = fold_rule(r"üstü|üstünde|üstündeki|üzeri|üzerinde|üzerindeki|fazla|büyük|yüksek|aşan|ve\s+üstü|ve\s+üzeri")
_MAXIMUM_SIDE = fold_rule(r"altı|altında|altındaki|az|küçük|düşük")
_RANGE = re.compile(rf"({_AMOUNT})\s*(?:ile|ila|-|–)\s*({_AMOUNT})\s+aras")
_AT_LEAST = re.compile(rf"\ben\s+az\s+({_AMOUNT})")
_AT_MOST = re.compile(fold_rule(rf"\ben\s+(?:fazla|çok)\s+({_AMOUNT})"))
_ABOVE = re.compile(rf"({_AMOUNT})\s+(?:{_MINIMUM_SIDE})\b")
_BELOW = re.compile(rf"({_AMOUNT})\s+(?:{_MAXIMUM_SIDE})\b")
# A period's count is no amount ("son 7 günde 500 TL üstü" has one amount).
_PERIOD_COUNT = re.compile(fold_rule(r"\bson\s+\d+\s+(?:gün|hafta|ay|yıl|sene)\w*"))

_RECEIVED = re.compile(fold_rule(r"\bkimden\b|\bkimlerden\b|\bgel(?:di|en|miş|ecek)|\baldı[mğ]"))
# CHAT-12 PR3: "havale yaptıklarım" (N24) and "para attım" in the first person only (K2-A): "Hasan para attı mı" says no direction.
_SENT = re.compile(fold_rule(
    r"\bkime\b|\bkimlere\b|\bgönder|\byolla|\bhavale\s+et|\bgiden|\beft\s+yap"
    r"|\b(?:havale|transfer)\s+yaptı(?:m|ğ|k)|\bat(?:tım|tığım|tıklarım|mışım)\b"))
# "Hasan bana para gönderdi mi?" (and "bana para atan") is money received, whatever the verb.
_TO_ME = re.compile(r"\bbana\b")
_ARRIVED = re.compile(fold_rule(r"\b(?:yattı|yatmış|geldi|gelmiş)\s*mı\b"))


@dataclass(frozen=True)
class Bounds:
    """The amount limit of a purchase search: inclusive, either side may be open; the currency is the amount's, lira when unsaid."""

    minimum: Decimal | None
    maximum: Decimal | None
    currency: str


def tool_of(folded: str) -> str | None:
    """The account tool the words of a folded message name; None when they name none (the other rules then decide)."""
    if _BALANCE.search(folded) and not _NOT_BALANCE.search(folded):
        return BALANCE
    if _INCOME.search(folded) and not _NOT_INCOME.search(folded):
        return INCOME
    if _TRANSFER.search(folded) and not re.search("harca", folded):
        return TRANSFERS
    if _SPENDING.search(folded) and amount_bounds(folded) is not None:
        return SEARCH
    return None


def amount_bounds(message: str) -> Bounds | None:
    """The amount limit the words give ("2.000 TL üstü", "500 liranın altında", "1.000 ile 5.000 TL arası"); None when there is
    none, or when the message writes more amounts than the limit uses (which one is the limit would be a guess)."""
    folded = fold_turkish(message)
    minimum = maximum = None
    used = []
    if (both := _RANGE.search(folded)) is not None:
        low, high = sorted((_value(both.group(1)), _value(both.group(2))))
        minimum, maximum, used = low, high, [both.group(0)]
    else:
        for pattern, side in ((_AT_LEAST, "min"), (_AT_MOST, "max"), (_ABOVE, "min"), (_BELOW, "max")):
            found = pattern.findall(folded)
            if not found:
                continue
            if len(found) > 1 or (side == "min" and minimum is not None) or (side == "max" and maximum is not None):
                return None
            if side == "min":
                minimum = _value(found[0])
            else:
                maximum = _value(found[0])
            used.append(found[0])
            # "en az 750" is also "750 ... az" for the side words; the first rule that reads an amount is its side.
            folded = folded.replace(found[0], " ", 1)
    if minimum is None and maximum is None:
        return None
    if len(amounts_in(_PERIOD_COUNT.sub(" ", fold_turkish(message)))) > (2 if both else len(used)):
        return None
    return Bounds(minimum, maximum, currency_in(" ".join(used)) or LIRA)


def transfer_direction(message: str) -> str | None:
    """"Sent" or "Received" as the words say; None for both or neither (then every transfer is listed)."""
    folded = fold_turkish(message)
    if _TO_ME.search(folded):
        return "Received"
    received, sent = _RECEIVED.search(folded) is not None, _SENT.search(folded) is not None
    return "Received" if received and not sent else "Sent" if sent and not received else None


def implied_this_month(tool: str, message: str) -> bool:
    """True for "maaşım yattı mı?" with no period (K6-C): the question is whether it arrived this month."""
    return tool in (INCOME, TRANSFERS) and not mentions_period(message) and _ARRIVED.search(fold_turkish(message)) is not None


def code_arguments(tool: str, arguments: dict, message: str, previous: dict | None) -> dict:
    """The call's arguments with the figures code reads itself: a search's limit, a transfer's direction. Whatever the model wrote
    for them is dropped; without them in the words a follow-up keeps the last call's (``previous``, already only what may carry)."""
    previous = previous or {}
    # A null the model wrote is no argument: the reply carries arguments as text, and "null" would reach the link as a name.
    arguments = {k: v for k, v in arguments.items() if v is not None}
    if tool == SEARCH:
        kept = {k: v for k, v in arguments.items() if k not in LIMIT_FIELDS}
        bounds = amount_bounds(message)
        if bounds is not None:
            if bounds.minimum is not None:
                kept["minAmount"] = json_number(bounds.minimum)
            if bounds.maximum is not None:
                kept["maxAmount"] = json_number(bounds.maximum)
            return {**kept, "currencyCode": bounds.currency}
        return {**kept, **{k: previous[k] for k in LIMIT_FIELDS if k in previous}}
    if tool == TRANSFERS:
        kept = {k: v for k, v in arguments.items() if k not in ("direction", "counterparty")}
        # K4-B: the model writes the name, but only one the user wrote (or the last call's, kept by a follow-up) is run.
        name = arguments.get("counterparty")
        if isinstance(name, str) and name.strip() and fold_turkish(name).strip() in fold_turkish(message):
            kept["counterparty"] = name.strip()
        elif "counterparty" in previous:
            kept["counterparty"] = previous["counterparty"]
        direction = transfer_direction(message) or previous.get("direction")
        return {**kept, "direction": direction} if direction else kept
    return dict(arguments)


def fallback_arguments(tool: str, message: str) -> dict | None:
    """The call the words chose when the model chose nothing (K2-C); None when its period must be asked."""
    if tool == BALANCE:
        return {}
    period = period_in(message)
    if period is None:
        return None
    return code_arguments(tool, {"period": period}, message, None)


def _value(text: str) -> Decimal:
    return amounts_in(text)[0]
