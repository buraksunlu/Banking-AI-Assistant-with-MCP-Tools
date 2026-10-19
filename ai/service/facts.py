"""Turns a tool's backend result into the facts the model explains, or into a fixed reply (AI-14a).

Amounts arrive as Decimal (the gateway parses JSON numbers that way) and leave as Turkish text (formatting.py).
"""

from dataclasses import dataclass
from datetime import date

from contracts import Fact
from formatting import money, percent, period

NO_SPENDING = "Bu dönemde harcama bulunamadı."
NO_MERCHANT_SPENDING = "Bu dönemde bu işyerinde harcama bulunamadı."
CURRENCIES_APART = "Farklı para birimleri toplanmaz, ayrı gösterilir."


@dataclass(frozen=True)
class ToolOutcome:
    """Facts for the model to explain, or a fixed reply that needs no model (``reply`` = (type, text))."""

    facts: list[Fact]
    reply: tuple[str, str] | None = None


def outcome_for(tool: str, arguments: dict, result: dict) -> ToolOutcome:
    """The facts (or fixed reply) for one tool result."""
    builders = {
        "get_spending_summary": _summary,
        "get_category_breakdown": _categories,
        "get_merchant_spending": _merchant,
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
    # Ambiguous and unknown names get a fixed sentence: the names come from the backend's closed set, so there is
    # nothing for the model to add and nothing it could get wrong (K3-A).
    if match["status"] == "Ambiguous":
        return ToolOutcome([], ("clarification",
                                f"‘{asked}’ birden fazla işyeriyle eşleşti: {', '.join(match['candidates'])}. "
                                "Hangisini kastettiniz?"))
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
