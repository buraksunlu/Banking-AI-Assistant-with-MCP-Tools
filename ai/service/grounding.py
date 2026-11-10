"""Checks that the arguments the model filled in come from the user's own words (CHAT-03, decision K2-A).

The input side of the answer check (AI-06): there every number in the answer must be in the facts, here every period and
count in a tool call must be in the message. A tool's period is required, so the model always writes one; "Ne kadar
harcadım?" became "this month" without the user knowing. MCP-17/18 add the user's own figures: a budget amount, its currency
and a cut in percent (K2-A, K3-A, K4-A). Standard library only, deterministic: a phrase the word lists miss leads to one
unnecessary question, never to a guessed figure.
"""

import re
from dataclasses import dataclass
from decimal import Decimal

from answer_check import fold_rule, fold_turkish, parse_turkish_numbers

# Any expression of time. Every rule here is written in Turkish and folded (fold_rule) like the text it reads (fold_turkish, K11-A).
_TIME = re.compile(fold_rule(
    r"\bbugün|\bdün"                               # bugün, dün, dünkü
    r"|\bhafta"                                     # hafta, haftanın, haftada
    r"|\bay(?:da|in|i|dan|ki|a)?\b"                 # ay, ayın, ayda, ayki; not "aylık" or "aylara" (no period in them)
    r"|\b(?:yil|sene)"                              # yıl, yılın, sene, senenin
    r"|\b\d+\s*gün|\bgünlük"                        # 30 gün, son 7 günde
    r"|\b(?:ocak|şubat|mart\b|marti|nisan|mayis|haziran|temmuz|ağustos|eylül|ekim|kasim|aralik)"  # month names, "mart" not "market"
))

_NUMBER_WORDS = {fold_rule(word): number for word, number in {
    "bir": 1, "iki": 2, "üç": 3, "dört": 4, "beş": 5, "alti": 6, "yedi": 7, "sekiz": 8, "dokuz": 9, "on": 10, "yirmi": 20,
    "otuz": 30}.items()}

# A number written the Turkish way: "1.000", "2.500,50", "2,5".
_NUMBER = r"\d{1,3}(?:\.\d{3})+(?:,\d+)?|\d+(?:,\d+)?"
# "5 bin", "1,5 bin", "bin" alone and, CHAT-10 K9-C, glued or with the endings people add ("5bin", "5 binlik"), "5 milyon" and
# "5k" (the "5k" of the INC-04 trial was read as 5). Written-out amounts ("bin beş yüz") are a documented limit (K3-A). Folded text.
_SCALED = re.compile(rf"(?:({_NUMBER})\s*)?(?<![a-z])(bin|milyon)(?:lik|luk|e|i|de|den)?(?![a-z])|({_NUMBER})\s?(k)(?![a-z])")
_SCALES = {"bin": 1000, "milyon": 1_000_000, "k": 1000}
# What may follow a number with no space and still leave it an amount: a currency code, a unit above, or "-lik" ("5000lik bütçe").
_GLUED = re.compile(rf"({_NUMBER})([a-z]+)")
_KNOWN_GLUED = re.compile(r"(?:tl|try|eur|usd|gbp|lik|luk)")

# Folded text, so plain letters only (K11-A); a letter must not touch the code ("atletizm" is no "tl"), a digit may ("3000TL").
_LETTER = "a-z"
_CURRENCIES = {
    "TRY": rf"(?<![{_LETTER}])(?:tl|try)(?![{_LETTER}])|\blira|₺",
    "EUR": rf"\beuro|\bavro|€|(?<![{_LETTER}])eur(?![{_LETTER}])",
    "USD": rf"\bdolar|\$|(?<![{_LETTER}])usd(?![{_LETTER}])",
    "GBP": rf"\bsterlin|\bpound|£|(?<![{_LETTER}])gbp(?![{_LETTER}])",
}

# A cut in percent: "%20", "20%", "yüzde 20", "yüzde yirmi beş", and three words people use instead of a number (K4-A).
_PERCENT_DIGITS = re.compile(fold_rule(r"%\s*(\d+)|(\d+)\s*%|\byüzde\s*(\d+)"))
# "yarın" (tomorrow) is not a half.
_PERCENT_WORDS = tuple((fold_rule(word), cut) for word, cut in (("yari(?!n)", 50), ("çeyre", 25), ("tamamen", 100)))
_UNITS = {fold_rule(word): number for word, number in {"bir": 1, "iki": 2, "üç": 3, "dört": 4, "beş": 5, "alti": 6, "yedi": 7,
                                                         "sekiz": 8, "dokuz": 9}.items()}
_TENS = {fold_rule(word): number for word, number in {
    "on": 10, "yirmi": 20, "otuz": 30, "kirk": 40, "elli": 50, "altmiş": 60, "yetmiş": 70, "seksen": 80, "doksan": 90,
    "yüz": 100}.items()}

# The words people use for each category code (INC-03 PR2 measurement: the model filled in OTHER and FOOD for "harcadığım parayla altın
# alsaydım"). Folded text; the codes are the server's closed set (ToolCategory), the words are only for checking that the user said it.
_CATEGORY_WORDS = {code: fold_rule(words) for code, words in {
    "FOOD": r"yeme|yemek|içme|restoran|lokanta|kafe|kahve",
    "GROCERY": r"market|bakkal|manav|kasap",
    "CLOTHING": r"giyim|kiyafet|elbise|ayakkabi",
    "TRANSPORTATION": r"ulaşim|ulasim|otobüs|metro|taksi|toplu\s+taşima",
    "ENTERTAINMENT": r"eğlence|eglence|sinema|konser|oyun",
    "SUBSCRIPTION": r"abonel|abone|üyelik|netflix|spotify",
    "TRAVEL": r"seyahat|tatil|otel|uçak",
    "FUEL": r"yakit|benzin|akaryakit|mazot",
    "BILLS": r"fatura|elektrik|doğalgaz|internet",
    "HEALTH": r"sağlik|saglik|eczane|hastane|doktor",
    "OTHER": r"diğer|diger|çeşitli",
}.items()}

# The tools' own periods (ToolPeriod), each with the words that name it; the order matters ("son 30 gün" before "gün").
_PERIODS = [(name, fold_rule(pattern)) for name, pattern in [
    ("Last30Days", r"\bson\s+(?:30|otuz)\s+gün"),
    ("Last7Days", r"\bson\s+(?:7|yedi)\s+gün"),
    ("Last3Months", r"\bson\s+(?:3|üç)\s+ay"),
    ("Last6Months", r"\bson\s+(?:6|alti)\s+ay"),
    ("ThisWeek", r"\bbu\s+hafta"),
    ("LastWeek", r"\bgeçen\s+hafta"),
    ("ThisMonth", r"\bbu\s+ay(?:da|in|i|ki)?\b"),
    ("LastMonth", r"\bgeçen\s+ay(?:da|in|i|ki)?\b"),
    ("ThisYear", r"\bbu\s+(?:yil|sene)"),
    ("Today", r"\bbugün"),
    ("Yesterday", r"\bdün"),
]]


@dataclass(frozen=True)
class Grounding:
    """``missing``: the first required argument the user did not give (asked back); ``unsaid``: all of them, in the tool's own
    order (MCP-17: an amount and a currency may both be invented); ``arguments``: what may run, with optional arguments the
    user did not give left out, so the backend's own default applies."""

    missing: str | None
    arguments: dict
    unsaid: tuple[str, ...] = ()


def mentions_period(message: str) -> bool:
    """True when the message says when: a day, week, month, year or month name."""
    return _TIME.search(fold_turkish(message)) is not None


def period_in(reply: str) -> str | None:
    """The tool period a reply names ("geçen ay" → "LastMonth"); None for anything else, never the nearest one."""
    folded = fold_turkish(reply)
    return next((name for name, pattern in _PERIODS if re.search(pattern, folded)), None)


def mentions_count(message: str, value: int) -> bool:
    """True when the message writes this count, in digits or words ("6", "altı"); a whole year ("son bir yıl", "geçen yıl")
    counts as 12, "yılda" (per year) and "bu yıl" (the months so far) do not."""
    folded = fold_turkish(message)
    if value in _counts(folded):
        return True
    return value == 12 and re.search(fold_rule(r"\b(?:son|geçen|bir|1)\s+(?:yil|sene)"), folded) is not None


def count_in(reply: str) -> int | None:
    """The first count a reply writes, in digits or words; None when there is none."""
    counts = _counts(fold_turkish(reply))
    return counts[0] if counts else None


def amounts_in(text: str) -> list[Decimal]:
    """Every amount a text writes, the Turkish way ("1.000", "2.500,50", "5 bin", "5k", "bin"), in order.

    A number with letters glued to it that are no currency or unit ("5x") is no amount at all (CHAT-10 K9-C): it is asked back by
    the caller rather than read as the digits alone.
    """
    folded = fold_turkish(text)
    found = [_decimal(m.group(1) or m.group(3) or "1") * _SCALES[m.group(2) or m.group(4)] for m in _SCALED.finditer(folded)]
    rest = _SCALED.sub(" ", folded)
    rest = _GLUED.sub(lambda m: m.group(0) if _KNOWN_GLUED.match(m.group(2)) else " ", rest)
    # The rest keeps its positions only roughly; the order of "5 bin" against a plain number never matters to a caller.
    return found + parse_turkish_numbers(rest)


def unclear_amounts(text: str) -> list[str]:
    """The numbers with letters glued to them that the code cannot read as an amount ("5x"), as the user wrote them."""
    rest = _SCALED.sub(" ", fold_turkish(text))
    return [m.group(0) for m in _GLUED.finditer(rest) if not _KNOWN_GLUED.match(m.group(2))]


def _decimal(number: str) -> Decimal:
    return Decimal(number.replace(".", "").replace(",", "."))


def json_number(amount: Decimal) -> int | float:
    """An amount in the types the model's own arguments arrive in; two decimals survive a float exactly as JSON text ("2500.5")."""
    return int(amount) if amount == amount.to_integral_value() else float(amount)


def mentions_amount(message: str, value) -> bool:
    """True when the message writes this amount; "1.000" is 1000, never 1 (the model's misreading K3-A exists for)."""
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return False
    return Decimal(str(value)) in amounts_in(message)


def currency_in(text: str) -> str | None:
    """The one currency a text names ("TL", "lira", "€", "dolar"…); None for none, or for two."""
    folded = fold_turkish(text)
    named = [code for code, pattern in _CURRENCIES.items() if re.search(pattern, folded)]
    return named[0] if len(named) == 1 else None


def amount_currency_in(text: str) -> str | None:
    """The currency written right next to an amount ("1.000 TL", "100 dolar", "$100"); None when no amount has one.

    INC-03 K17-B: "1.000 TL'yi dolar olarak" names two currencies, but only TL is the amount's; the other is the asset. An amount
    with no currency beside it is not guessed from the rest of the sentence.
    """
    folded = fold_turkish(text)
    found = []
    for code, pattern in _CURRENCIES.items():
        for shape in (rf"\d[\d.,]*\s*(?:bin\s*)?(?:{pattern})", rf"(?:{pattern})\s*\d"):
            if match := re.search(shape, folded):
                found.append((match.start(), code))
    return min(found)[1] if found else None


def mentions_category(message: str, code) -> bool:
    """True when the message names this category in the words people use for it ("eğlence", "yeme içme", "market")."""
    pattern = _CATEGORY_WORDS.get(code) if isinstance(code, str) else None
    return pattern is not None and re.search(rf"\b(?:{pattern})", fold_turkish(message)) is not None


def category_in(message: str) -> str | None:
    """The one category code the message names in people's words ("eğlence" → ENTERTAINMENT); None for none, or for two."""
    folded = fold_turkish(message)
    named = [code for code, words in _CATEGORY_WORDS.items() if re.search(rf"\b(?:{words})", folded)]
    return named[0] if len(named) == 1 else None


def mentions_currency(message: str, code) -> bool:
    """True when the message names this currency and no other."""
    return isinstance(code, str) and currency_in(message) == code


def percent_in(text: str) -> int | None:
    """The cut a text names ("%20", "yüzde yirmi", "yarıya" = 50); None when there is none."""
    folded = fold_turkish(text)
    digits = _PERCENT_DIGITS.search(folded)
    if digits:
        return int(next(group for group in digits.groups() if group))
    words = re.search(fold_rule(r"\byüzde\s+([a-zçğöşü]+)(?:\s+([a-zçğöşü]+))?"), folded)
    if words and words.group(1) in _TENS:
        return _TENS[words.group(1)] + _UNITS.get(words.group(2) or "", 0)
    return next((value for word, value in _PERCENT_WORDS if re.search(rf"\b{word}", folded)), None)


def mentions_percent(message: str, value) -> bool:
    """True when the message names this cut."""
    return isinstance(value, int) and not isinstance(value, bool) and percent_in(message) == value


def check_arguments(arguments: dict, schema: dict, message: str, previous: dict | None = None) -> Grounding:
    """Compares a tool call with the message, using the tool's published schema (which arguments are counts, which required).

    In a follow-up (CHAT-02) ``previous`` holds the last call's arguments: a value carried over unchanged is grounded too
    ("Peki Kahve Durağı'na?" keeps last month), one that is neither said nor carried over is still a guess.
    """
    properties, required = schema.get("properties", {}), set(schema.get("required", []))
    previous = previous or {}
    kept, unsaid = dict(arguments), set()
    for name, value in arguments.items():
        if name == "period" and (said := _said_period(value, properties.get(name, {}), message)) is not None:
            # CHAT-09 K12-A: a period the user named decides, before anything carried over: the model's Last7Days for "geçen
            # hafta" would answer for other days, and the check used to ask only whether some period was said.
            if said:
                kept[name] = said
            elif name in required:
                unsaid.add(name)
            else:
                del kept[name]
            continue
        if name not in required and isinstance(value, str) and not value.strip():
            # A blank optional argument is the model's way of leaving it out; the server would refuse "" as a code.
            del kept[name]
            continue
        if name == "merchant" and name in previous and not _named(value, message):
            # A merchant the user did not name in a follow-up is the last call's: the model fell back to an older one.
            kept[name] = previous[name]
            continue
        if (name in previous and previous[name] == value) or _grounded(name, value, properties.get(name, {}), message):
            continue
        if name in required:
            unsaid.add(name)
        else:
            del kept[name]
    # A figure the model left out entirely is asked as well (MCP-17/18); other required arguments keep the server's refusal.
    unsaid.update(name for name in required if name not in arguments and _figure(name, properties.get(name, {})))
    order = [name for name in properties if name in unsaid]
    return Grounding(order[0] if order else None, dict(arguments) if order else kept, tuple(order))


def periods_in(message: str) -> list[str]:
    """Every tool period the message names, in the table's order ("bu hafta mı geçen hafta mı" → ThisWeek, LastWeek)."""
    folded = fold_turkish(message)
    return [name for name, pattern in _PERIODS if re.search(pattern, folded)]


def _said_period(value, rule: dict, message: str) -> str | None:
    """None when the message names no tool period (the other checks decide); otherwise the period to run: the model's when it is
    one the user named, the user's when they named exactly one the tool offers, "" (asked) for anything else."""
    named = periods_in(message)
    if not named:
        return None
    if value in named:
        return value
    offered = rule.get("enum")
    return named[0] if len(named) == 1 and (offered is None or named[0] in offered) else ""


def _figure(name: str, rule: dict) -> bool:
    return name in ("currencyCode", "reductionPercent") or _has_type(rule, "number")


def _has_type(rule: dict, name: str) -> bool:
    # An optional argument is published as ["number", "null"] (MCP-17/18): the type is a list, and used to slip past the checks.
    declared = rule.get("type")
    return declared == name or (isinstance(declared, list) and name in declared)


def _grounded(name: str, value, rule: dict, message: str) -> bool:
    if name == "period":
        return mentions_period(message)
    if name == "currencyCode":
        return mentions_currency(message, value)
    if name == "categoryCode":
        return mentions_category(message, value)
    if name == "reductionPercent":
        return mentions_percent(message, value)
    if _has_type(rule, "number"):
        return mentions_amount(message, value)
    if _has_type(rule, "integer"):
        return isinstance(value, int) and mentions_count(message, value)
    # Merchant names are matched by the backend against the customer's own merchants (MerchantMatcher), not here; a
    # category code is checked against the closed set by the MCP server.
    return True


def _named(value, message: str) -> bool:
    return isinstance(value, str) and fold_turkish(value).strip() in fold_turkish(message)


def _counts(folded: str) -> list[int]:
    words = re.findall(fold_rule(r"\d+|[a-zçğöşü]+"), folded)
    found, skip = [], False
    for word, following in zip(words, [*words[1:], ""]):
        if skip:
            skip = False
        elif word.isdigit():
            found.append(int(word))
        elif word in _NUMBER_WORDS:
            # "on iki" is 12; "on" alone is 10.
            if word == "on" and _NUMBER_WORDS.get(following, 10) < 10:
                found.append(10 + _NUMBER_WORDS[following])
                skip = True
            else:
                found.append(_NUMBER_WORDS[word])
    return found
