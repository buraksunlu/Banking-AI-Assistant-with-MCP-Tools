"""Turns a tool's backend result into the facts the model explains, or into a fixed reply (AI-14a, MCP-08).

Amounts arrive as Decimal (the gateway parses JSON numbers that way) and leave as Turkish text (formatting.py). Nothing is
added up or computed here: every amount, share and change is the backend's. The bank's raw transaction text never
becomes a fact (AI-13): it comes from outside the bank and may carry an instruction.
"""

from dataclasses import dataclass
from datetime import date

from contracts import Fact
from formatting import day, money, month, percent, period

NO_SPENDING = "Bu dönemde harcama bulunamadı."
NO_MERCHANT_SPENDING = "Bu dönemde bu işyerinde harcama bulunamadı."
CURRENCIES_APART = "Farklı para birimleri toplanmaz, ayrı gösterilir."
NO_SPENDING_IN_EITHER = "Bu iki dönemde harcama bulunamadı."
NO_RECURRING = "Son 6 ayda düzenli bir ödeme bulunamadı."
NO_UPCOMING = "Bu dönemde beklenen düzenli ödeme yok."
UNCLASSIFIED = "Sınıflandırılmamış"
RECURRING_RULE = ("Düzenli ödemeler son 6 aydaki harcamalardan bulunur (en az 3 ödeme, yaklaşık aylık aralık); "
                  "sonraki ödeme tarihleri tahminidir, kesin değildir.")
UPCOMING_RULE = ("Tarih ve tutarlar düzenli ödemelerin geçmişine göre tahmindir (son ödeme tutarı, her ayın aynı günü); "
                 "kesin değildir.")
# K6-B: a long list keeps the model's facts short; only the number of the rest is said, never an amount added up here.
LIST_LIMIT = 5


@dataclass(frozen=True)
class ToolOutcome:
    """Facts for the model to explain, a fixed reply that needs no model (``reply`` = (type, text)), or the merchants the
    user has to choose from (``candidates``)."""

    facts: list[Fact]
    reply: tuple[str, str] | None = None
    candidates: tuple[str, ...] = ()


def outcome_for(tool: str, arguments: dict, result: dict) -> ToolOutcome:
    """The facts (or fixed reply) for one tool result."""
    builders = {
        "get_spending_summary": _summary,
        "get_category_breakdown": _categories,
        "get_merchant_spending": _merchant,
        "get_merchant_breakdown": _merchant_breakdown,
        "get_highest_transactions": _highest,
        "compare_spending": _comparison,
        "get_monthly_trend": _trend,
        "get_recurring_payments": _recurring,
        "get_upcoming_payments": _upcoming,
    }
    return builders[tool](arguments, result)


def _summary(arguments: dict, result: dict) -> ToolOutcome:
    facts = [_period(result)]
    rows = result["currencies"]
    for row in rows:
        suffix = _suffix(row, rows)
        facts.append(_fact(f"Toplam harcama{suffix}", money(row["totalAmount"], row["currencyCode"])))
        facts.append(_fact(f"İşlem sayısı{suffix}", str(row["transactionCount"])))
        if row["uncategorizedAmount"] > 0:
            facts.append(_fact(f"Sınıflandırılmamış{suffix}", money(row["uncategorizedAmount"], row["currencyCode"])))
    return ToolOutcome(_closing(facts, rows, NO_SPENDING))


def _categories(arguments: dict, result: dict) -> ToolOutcome:
    facts = [_period(result)]
    rows = result["currencies"]
    for row in rows:
        suffix = _suffix(row, rows)
        facts.append(_fact(f"Toplam harcama{suffix}", money(row["totalAmount"], row["currencyCode"])))
        for category in row["categories"]:
            text = f"{money(category['amount'], row['currencyCode'])} ({percent(category['percentage'])})"
            facts.append(_fact(f"{category['displayName']}{suffix}", text))
    return ToolOutcome(_closing(facts, rows, NO_SPENDING))


def _merchant(arguments: dict, result: dict) -> ToolOutcome:
    asked = arguments["merchant"]
    match = result["match"]
    # Several merchants: the candidates (the backend's closed set) go back to the chat, which asks and remembers the
    # question (CHAT-03). An unknown name gets a fixed sentence: nothing for the model to add (AI-14a K3-A).
    if match["status"] == "Ambiguous":
        return ToolOutcome([], candidates=tuple(match["candidates"]))
    if match["status"] == "NotFound":
        return ToolOutcome([], ("answer", f"‘{asked}’ adında bir işyerinde harcamanız bulunmuyor."))

    facts = [_period(result), _fact("İşyeri", match["merchant"])]
    if match["isPartialMatch"]:
        facts.append(_fact("Not", f"‘{asked}’ yazımı ‘{match['merchant']}’ ile eşleşti."))
    rows = result["currencies"]
    for row in rows:
        suffix = _suffix(row, rows)
        facts.append(_fact(f"Toplam harcama{suffix}", money(row["totalAmount"], row["currencyCode"])))
        facts.append(_fact(f"İşlem sayısı{suffix}", str(row["transactionCount"])))
    return ToolOutcome(_closing(facts, rows, NO_MERCHANT_SPENDING))


def _merchant_breakdown(arguments: dict, result: dict) -> ToolOutcome:
    facts = [_period(result)]
    rows = result["currencies"]
    for row in rows:
        suffix = _suffix(row, rows)
        facts.append(_fact(f"Toplam harcama{suffix}", money(row["totalAmount"], row["currencyCode"])))
        for merchant in row["merchants"][:LIST_LIMIT]:
            text = (f"{money(merchant['amount'], row['currencyCode'])} ({percent(merchant['percentage'])}; "
                    f"{merchant['transactionCount']} işlem)")
            facts.append(_fact(f"{merchant['displayName']}{suffix}", text))
        facts.extend(_rest(row["merchants"], "işyeri", suffix))
    return ToolOutcome(_closing(facts, rows, NO_SPENDING))


def _highest(arguments: dict, result: dict) -> ToolOutcome:
    facts = [_period(result)]
    rows = result["currencies"]
    for row in rows:
        suffix = _suffix(row, rows)
        for rank, item in enumerate(row["items"][:LIST_LIMIT], start=1):
            # Only the backend's own fields: amount, merchant, category and date; rawDescription is left out on purpose.
            where = item["merchant"] or UNCLASSIFIED
            if item["categoryDisplayName"]:
                where += f" ({item['categoryDisplayName']})"
            text = f"{money(item['amount'], item['currencyCode'])}; {where}; {day(date.fromisoformat(item['localDate']))}"
            facts.append(_fact(f"{rank}. harcama{suffix}", text))
    return ToolOutcome(_closing(facts, rows, NO_SPENDING))


def _comparison(arguments: dict, result: dict) -> ToolOutcome:
    facts = [_fact("Dönem", _range(result["current"])), _fact("Önceki dönem", _range(result["previous"]))]
    rows = result["currencies"]
    for row in rows:
        suffix, currency = _suffix(row, rows), row["currencyCode"]
        facts.append(_fact(f"Dönem harcaması{suffix}", f"{money(row['currentAmount'], currency)} ({row['currentCount']} işlem)"))
        facts.append(_fact(f"Önceki dönem harcaması{suffix}",
                           f"{money(row['previousAmount'], currency)} ({row['previousCount']} işlem)"))
        share = row["percentageChange"]
        # No percentage from the backend means the previous period was empty: a rate from zero is never invented.
        rate = (f"({percent(abs(share))})" if share is not None
                else "(önceki dönemde harcama olmadığı için yüzde hesaplanmaz)")
        facts.append(_fact(f"Değişim{suffix}", f"{_change(row['change'], currency)} {rate}"))
        changed = [c for c in row["categories"] if c["change"] != 0]
        for category in changed[:LIST_LIMIT]:
            text = (f"{money(category['currentAmount'], currency)}, önceki dönem {money(category['previousAmount'], currency)}: "
                    f"{_change(category['change'], currency)}")
            facts.append(_fact(f"{category['displayName']}{suffix}", text))
        facts.extend(_rest(changed, "kategori", suffix))
    return ToolOutcome(_closing(facts, rows, NO_SPENDING_IN_EITHER))


def _trend(arguments: dict, result: dict) -> ToolOutcome:
    windows = result["months"]
    first, last = windows[0]["period"], windows[-1]["period"]
    facts = [_fact("Dönem", period(date.fromisoformat(first["startDate"]), date.fromisoformat(last["endDate"])))]
    ranges = {(w["year"], w["month"]): w["period"] for w in windows}
    rows = result["currencies"]
    for row in rows:
        suffix = _suffix(row, rows)
        for point in row["months"]:
            label = month(point["year"], point["month"])
            if point["isPartial"]:
                label += f" (devam eden ay, {_range(ranges[(point['year'], point['month'])])})"
            facts.append(_fact(f"{label}{suffix}", money(point["amount"], row["currencyCode"])))
    return ToolOutcome(_closing(facts, rows, NO_SPENDING))


def _recurring(arguments: dict, result: dict) -> ToolOutcome:
    facts = [_fact("İncelenen dönem", _range(result["period"]))]
    payments = result["payments"]
    for payment in payments[:LIST_LIMIT]:
        rule, currency = payment["pattern"], payment["currencyCode"]
        text = (f"{money(rule['latestAmount'], currency)} / ay ({payment['categoryDisplayName']}); "
                f"{rule['occurrenceCount']} ödeme, son ödeme {_day(rule['lastDate'])}; "
                f"tahmini sonraki ödeme {_day(rule['nextExpectedDate'])}")
        facts.append(_fact(payment["merchant"], text))
    facts.extend(_rest(payments, "düzenli ödeme", ""))
    totals = result["monthlyTotals"]
    for total in totals:
        suffix = f" ({total['currencyCode']})" if len(totals) > 1 else ""
        facts.append(_fact(f"Aylık toplam{suffix}", money(total["monthlyTotal"], total["currencyCode"])))
    if not payments:
        facts.append(_fact("Sonuç", NO_RECURRING))
    elif len(totals) > 1:
        facts.append(_fact("Not", CURRENCIES_APART))
    return ToolOutcome([*facts, _fact("Not", RECURRING_RULE)])


def _upcoming(arguments: dict, result: dict) -> ToolOutcome:
    facts = [_fact("Dönem", period(date.fromisoformat(result["from"]), date.fromisoformat(result["to"])))]
    payments = result["payments"]
    for payment in payments[:LIST_LIMIT]:
        text = (f"tahmini {money(payment['estimatedAmount'], payment['currencyCode'])}; "
                f"tahmini tarih {_day(payment['estimatedDate'])} ({_when(payment['daysUntil'], payment['isOverdue'])})")
        facts.append(_fact(payment["merchant"], text))
    facts.extend(_rest(payments, "ödeme", ""))
    totals = result["totals"]
    for total in totals:
        suffix = f" ({total['currencyCode']})" if len(totals) > 1 else ""
        facts.append(_fact(f"Tahmini toplam{suffix}", money(total["estimatedTotal"], total["currencyCode"])))
    if not payments:
        facts.append(_fact("Sonuç", NO_UPCOMING))
    elif len(totals) > 1:
        facts.append(_fact("Not", CURRENCIES_APART))
    return ToolOutcome([*facts, _fact("Not", UPCOMING_RULE)])


def _rest(items: list, noun: str, suffix: str) -> list[Fact]:
    hidden = len(items) - LIST_LIMIT
    return [_fact(f"Not{suffix}", f"Bu dönemde {hidden} {noun} daha var; ilk {LIST_LIMIT} {noun} gösterildi.")] if hidden > 0 else []


def _change(amount, currency: str) -> str:
    # The sign becomes a word: "-484,70 TL" is easy to misread, "484,70 TL azalış" is not.
    if amount == 0:
        return "değişmedi"
    return f"{money(abs(amount), currency)} {'artış' if amount > 0 else 'azalış'}"


def _when(days: int, overdue: bool) -> str:
    if overdue:
        return f"{abs(days)} gün gecikti, henüz ödenmedi"
    return "bugün" if days == 0 else f"{days} gün sonra"


def _range(resolved: dict) -> str:
    return period(date.fromisoformat(resolved["startDate"]), date.fromisoformat(resolved["endDate"]))


def _day(text: str) -> str:
    return day(date.fromisoformat(text))


def _period(result: dict) -> Fact:
    start, end = (date.fromisoformat(result["period"][key]) for key in ("startDate", "endDate"))
    return _fact("Dönem", period(start, end))


def _suffix(row: dict, rows: list[dict]) -> str:
    # With one currency the label stays plain ("Toplam harcama"); with several each line names its currency.
    return f" ({row['currencyCode']})" if len(rows) > 1 else ""


def _closing(facts: list[Fact], rows: list[dict], empty_text: str) -> list[Fact]:
    # The model only knows what the facts say: an empty result has to be stated, and so does "not added up".
    if not rows:
        return [*facts, _fact("Sonuç", empty_text)]
    if len(rows) > 1:
        return [*facts, _fact("Not", CURRENCIES_APART)]
    return facts


def _fact(label: str, text: str) -> Fact:
    return Fact(label=label, text=text)
