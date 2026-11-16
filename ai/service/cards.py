"""The answer cards (UX-01 PR4): a tool result turned into a structured card by code, never by the model.

A card holds a headline figure, an optional change against the period before, the blocks of its simple and detailed parts and
one everyday sentence (design K1-A: a fixed pattern per tool). Every figure and text comes from the backend result in the
formatting of formatting.py; the only number made here is a bar's ``share`` (the row against the card's largest row, K3-A),
which is drawn and never written. One card per currency: currencies are never added up. A result the code cannot read gives
no card, never an error: the text answer still stands.
"""

import calendar
import logging
from dataclasses import dataclass
from datetime import date
from decimal import ROUND_HALF_UP, Decimal

from formatting import day, money, month, percent, period

logger = logging.getLogger(__name__)

# K7-A: the simple part shows the first three rows; the facts list five (facts.LIST_LIMIT), so no row has a figure they lack.
SIMPLE_ROWS = 3
FACT_ROWS = 5
SHORT_MONTHS = ("Oca", "Şub", "Mar", "Nis", "May", "Haz", "Tem", "Ağu", "Eyl", "Eki", "Kas", "Ara")


@dataclass(frozen=True)
class Headline:
    """The big figure of a card."""

    amount: Decimal
    currency: str
    text: str


@dataclass(frozen=True)
class Change:
    """The change against the period before; ``percent`` is None when that period had nothing to compare with."""

    percent: Decimal | None
    direction: str
    against: str
    against_text: str
    text: str


@dataclass(frozen=True)
class Bar:
    """One row of a ``bars`` block: ``share`` is the bar's length, 0-1; ``direction`` only for a change."""

    label: str
    amount: Decimal
    text: str
    share: Decimal
    direction: str | None = None


@dataclass(frozen=True)
class Point:
    """One month of a ``series`` block; ``partial`` marks the running month."""

    label: str
    amount: Decimal
    text: str
    share: Decimal
    partial: bool


@dataclass(frozen=True)
class Stat:
    """One cell of a ``stats`` block."""

    label: str
    text: str


@dataclass(frozen=True)
class Block:
    """A part of a card: ``bars`` uses ``rows``, ``series`` ``points`` (and ``highlight``), ``stats`` ``items``."""

    kind: str
    rows: tuple[Bar, ...] = ()
    points: tuple[Point, ...] = ()
    items: tuple[Stat, ...] = ()
    highlight: str | None = None


@dataclass(frozen=True)
class Card:
    """One answer card; ``detailed`` is empty when the tool has nothing more to show."""

    title: str
    headline: Headline
    change: Change | None
    simple: tuple[Block, ...]
    detailed: tuple[Block, ...]
    sentence: str


def cards_for(tool: str, arguments: dict, result: dict) -> list[Card]:
    """The cards of one tool result, one per currency; [] for a tool without cards or a result the code cannot read."""
    build = _BUILDERS.get(tool)
    if build is None:
        return []
    try:
        rows = result["currencies"]
        return [build(arguments, result, row, _suffix(row, rows)) for row in rows if _has_card(tool, result)]
    except (KeyError, TypeError, ValueError, IndexError) as error:
        # A result of another shape (a server newer than this code): the text answer is enough, the turn goes on.
        logger.warning("No card for %s: %s", tool, type(error).__name__)
        return []


def card_texts(card: Card) -> list[str]:
    """Every text a card shows, for the number check (K6-A)."""
    texts = [card.title, card.headline.text, card.sentence]
    if card.change is not None:
        texts += [card.change.against, card.change.against_text, card.change.text]
    for block in (*card.simple, *card.detailed):
        texts += [t for bar in block.rows for t in (bar.label, bar.text)]
        texts += [t for point in block.points for t in (point.label, point.text)]
        texts += [t for stat in block.items for t in (stat.label, stat.text)]
    return texts


def _has_card(tool: str, result: dict) -> bool:
    # An ambiguous or unknown merchant is asked back or answered by a fixed text: there is no figure to show.
    return tool != "get_merchant_spending" or result["match"]["status"] == "Matched"


def _summary(arguments: dict, result: dict, row: dict, suffix: str) -> Card:
    label, currency = _label(result["period"]), row["currencyCode"]
    total = money(row["totalAmount"], currency)
    items = [Stat("İşlem", str(row["transactionCount"]))]
    if row["uncategorizedAmount"] > 0:
        items.append(Stat("Sınıflandırılmamış", money(row["uncategorizedAmount"], currency)))
    return Card(f"Harcama · {label}{suffix}", _headline(row["totalAmount"], currency), None,
                (Block("stats", items=tuple(items)),), (), f"{label}: {row['transactionCount']} harcama, toplam {total}.")


def _categories(arguments: dict, result: dict, row: dict, suffix: str) -> Card:
    currency, categories = row["currencyCode"], row["categories"]
    bars = _bars([(c["displayName"], c["amount"], f"{money(c['amount'], currency)} · {percent(c['percentage'])}", None)
                  for c in categories])
    sentence = f"En büyük pay {categories[0]['displayName']}: {percent(categories[0]['percentage'])}." if categories else ""
    return Card(f"Kategoriler · {_label(result['period'])}{suffix}", _headline(row["totalAmount"], currency), None,
                _part(bars[:SIMPLE_ROWS]), _part(bars[SIMPLE_ROWS:]), sentence)


def _category_spending(arguments: dict, result: dict, row: dict, suffix: str) -> Card:
    name, currency, current = result["displayName"], row["currencyCode"], result["period"]
    merchants = row["merchants"][:FACT_ROWS]
    bars = _bars([(m["displayName"], m["amount"], f"{money(m['amount'], currency)} · {percent(m['percentage'])}", None)
                  for m in merchants])
    points = _points(row["months"], currency)
    items = [Stat("6 ay ortalaması", money(row["monthlyAverage"], currency))]
    if (versus := row["percentageVsAverage"]) is not None:
        items.append(Stat("Ortalamaya göre", _versus(versus)))
    items.append(Stat("İşlem", str(row["transactionCount"])))
    if row["averagePerTransaction"] is not None:
        items.append(Stat("İşlem başı", money(row["averagePerTransaction"], currency)))
    if (largest := row["largest"]) is not None:
        where = largest["merchant"] or "Sınıflandırılmamış"
        items.append(Stat("En büyük işlem", f"{money(largest['amount'], currency)} · {where} · {day(date.fromisoformat(largest['localDate']))}"))
    if row["shareOfSpending"] is not None:
        items.append(Stat("Harcamadaki payı", percent(row["shareOfSpending"])))
    highlight = SHORT_MONTHS[_start(current).month - 1] if _is_whole_month(current) else None
    detailed = (Block("series", points=points, highlight=highlight), Block("stats", items=tuple(items)))
    change = _change(row["change"], row["percentageChange"], result["previousPeriod"], row["previousAmount"], currency)
    if row["transactionCount"] == 0:
        sentence = f"{name}: bu dönemde harcama yok."
    else:
        sentence = f"{name}: önceki döneme göre {_more_or_less(row['change'], currency)}."
    return Card(f"{name} · {_label(current)}{suffix}", _headline(row["amount"], currency), change, _part(bars), detailed, sentence)


def _merchant(arguments: dict, result: dict, row: dict, suffix: str) -> Card:
    name, currency = result["match"]["merchant"], row["currencyCode"]
    count, total = row["transactionCount"], money(row["totalAmount"], row["currencyCode"])
    return Card(f"{name} · {_label(result['period'])}{suffix}", _headline(row["totalAmount"], currency), None,
                (Block("stats", items=(Stat("İşlem", str(count)),)),), (), f"{name}: {count} harcama, toplam {total}.")


def _comparison(arguments: dict, result: dict, row: dict, suffix: str) -> Card:
    currency = row["currencyCode"]
    # The drivers the facts list: changed categories only, biggest change first, at most five.
    changed = [c for c in row["categories"] if c["change"] != 0][:FACT_ROWS]
    bars = _bars([(c["displayName"], abs(c["change"]), _signed(c["change"], currency), _direction(c["change"])) for c in changed])
    change = _change(row["change"], row["percentageChange"], result["previous"], row["previousAmount"], currency)
    if changed:
        sentence = f"En büyük değişim {changed[0]['displayName']}: {_signed(changed[0]['change'], currency)}."
    else:
        sentence = "Kategorilerde değişim yok."
    return Card(f"Karşılaştırma · {_label(result['current'])}{suffix}", _headline(row["currentAmount"], currency), change,
                _part(bars[:SIMPLE_ROWS]), _part(bars[SIMPLE_ROWS:]), sentence)


def _trend(arguments: dict, result: dict, row: dict, suffix: str) -> Card:
    currency, months = row["currencyCode"], row["months"]
    complete = [m for m in months if not m["isPartial"]]
    last = complete[-1]
    # K5-D: the highest and the lowest whole month are chosen, not computed; the first one wins a tie.
    highest = max(complete, key=lambda m: m["amount"])
    lowest = min(complete, key=lambda m: m["amount"])
    items = (Stat("En yüksek ay", f"{month(highest['year'], highest['month'])} · {money(highest['amount'], currency)}"),
             Stat("En düşük ay", f"{month(lowest['year'], lowest['month'])} · {money(lowest['amount'], currency)}"))
    series = Block("series", points=_points(months, currency), highlight=SHORT_MONTHS[last["month"] - 1])
    sentence = f"Son tam ay {month(last['year'], last['month'])}: {money(last['amount'], currency)}."
    windows = result["months"]
    span = period(_start(windows[0]["period"]), date.fromisoformat(windows[-1]["period"]["endDate"]))
    return Card(f"Aylık harcama · {span}{suffix}", _headline(last["amount"], currency), None, (series,),
                (Block("stats", items=items),), sentence)


_BUILDERS = {
    "get_spending_summary": _summary,
    "get_category_breakdown": _categories,
    "get_category_spending": _category_spending,
    "get_merchant_spending": _merchant,
    "compare_spending": _comparison,
    "get_monthly_trend": _trend,
}


def _headline(amount: Decimal, currency: str) -> Headline:
    return Headline(amount, currency, money(amount, currency))


def _bars(rows: list[tuple[str, Decimal, str, str | None]]) -> list[Bar]:
    largest = max((amount for _, amount, _, _ in rows), default=Decimal(0))
    return [Bar(label, amount, text, _share(amount, largest), direction) for label, amount, text, direction in rows]


def _points(months: list[dict], currency: str) -> tuple[Point, ...]:
    largest = max((m["amount"] for m in months), default=Decimal(0))
    return tuple(Point(SHORT_MONTHS[m["month"] - 1], m["amount"], money(m["amount"], currency), _share(m["amount"], largest),
                       m["isPartial"]) for m in months)


def _share(amount: Decimal, largest: Decimal) -> Decimal:
    # K3-A: a bar's length against the card's largest row, for drawing only; nothing written comes from it.
    if largest <= 0:
        return Decimal(0)
    return (amount / largest).quantize(Decimal("0.01"), ROUND_HALF_UP)


def _part(bars: list[Bar]) -> tuple[Block, ...]:
    return (Block("bars", rows=tuple(bars)),) if bars else ()


def _change(delta: Decimal, rate: Decimal | None, previous: dict, previous_amount: Decimal, currency: str) -> Change:
    direction = _direction(delta)
    if rate is None:
        text = "önceki dönemde harcama yok"
    else:
        text = f"{percent(abs(rate))} {'artış' if delta > 0 else 'azalış'}" if delta != 0 else "değişmedi"
    return Change(rate, direction, _label(previous), money(previous_amount, currency), text)


def _direction(delta: Decimal) -> str:
    return "up" if delta > 0 else "down" if delta < 0 else "same"


def _signed(delta: Decimal, currency: str) -> str:
    # Like the facts: the sign is a word, "824,00 TL azalış", so no minus sign is read as part of a figure.
    if delta == 0:
        return "değişmedi"
    return f"{money(abs(delta), currency)} {'artış' if delta > 0 else 'azalış'}"


def _more_or_less(delta: Decimal, currency: str) -> str:
    if delta == 0:
        return "değişmedi"
    return f"{money(abs(delta), currency)} {'fazla' if delta > 0 else 'az'}"


def _versus(value: Decimal) -> str:
    if value == 0:
        return "ortalamayla aynı"
    return f"{percent(abs(value))} {'üstünde' if value > 0 else 'altında'}"


def _label(resolved: dict) -> str:
    """A whole calendar month by its name ("Eylül 2026"), any other period by its days ("1-10 Ekim 2026")."""
    start = _start(resolved)
    if _is_whole_month(resolved):
        return month(start.year, start.month)
    return period(start, date.fromisoformat(resolved["endDate"]))


def _start(resolved: dict) -> date:
    return date.fromisoformat(resolved["startDate"])


def _is_whole_month(resolved: dict) -> bool:
    start, end = _start(resolved), date.fromisoformat(resolved["endDate"])
    return start.day == 1 and end == start.replace(day=calendar.monthrange(start.year, start.month)[1])


def _suffix(row: dict, rows: list[dict]) -> str:
    # One currency: a plain title; several: each card names its own, so two cards never read as one total.
    return f" ({row['currencyCode']})" if len(rows) > 1 else ""
