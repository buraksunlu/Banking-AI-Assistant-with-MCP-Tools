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

from facts import (BUDGET_RULE, GROUP_LABELS, NO_PRICE, RECURRING_RULE, SAVINGS_RULE, UNCLASSIFIED, UPCOMING_RULE, alternatives_line,
                   asset_name, change_text, volatility_facts, when as _when)
from formatting import day, money, month, percent, period

logger = logging.getLogger(__name__)

# K7-A: the simple part shows the first three rows; the facts list five (facts.LIST_LIMIT), so no row has a figure they lack.
SIMPLE_ROWS = 3
FACT_ROWS = 5
SHORT_MONTHS = ("Oca", "Şub", "Mar", "Nis", "May", "Haz", "Tem", "Ağu", "Eyl", "Eki", "Kas", "Ara")


@dataclass(frozen=True)
class Headline:
    """The big figure of a card; ``amount`` and ``currency`` are None when the figure is not money (a share, UX-01 PR6 K7-B)."""

    amount: Decimal | None
    currency: str | None
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
class Entry:
    """One line of a ``list`` block (UX-01 PR6a): a payment, a purchase; ``detail`` is its second line, ``direction`` an arrow."""

    label: str
    detail: str
    text: str
    direction: str | None = None


@dataclass(frozen=True)
class Block:
    """A part of a card: ``bars`` uses ``rows``, ``series`` ``points`` (and ``highlight``), ``stats`` ``items``, ``list``
    ``entries``."""

    kind: str
    rows: tuple[Bar, ...] = ()
    points: tuple[Point, ...] = ()
    items: tuple[Stat, ...] = ()
    highlight: str | None = None
    entries: tuple[Entry, ...] = ()


@dataclass(frozen=True)
class Card:
    """One answer card; ``detailed`` is empty when the tool has nothing more to show."""

    title: str
    headline: Headline
    change: Change | None
    simple: tuple[Block, ...]
    detailed: tuple[Block, ...]
    sentence: str
    # UX-01 PR6 K4-A: code's own fixed sentences (an estimate's rule, an investment warning), always shown under the card.
    notes: tuple[str, ...] = ()


def cards_for(tool: str, arguments: dict, result: dict) -> list[Card]:
    """The cards of one tool result, one per currency; [] for a tool without cards or a result the code cannot read."""
    build, whole = _BUILDERS.get(tool), _WHOLE_BUILDERS.get(tool)
    if build is None and whole is None:
        return []
    try:
        if whole is not None:
            return whole(arguments, result)
        rows = result["currencies"]
        if tool in ("get_income_share", "get_spending_advice"):
            rows = [r for r in rows if r.get("incomeBase") is not None and r.get("reason") != "NoIncome"]
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
        texts += [t for entry in block.entries for t in (entry.label, entry.detail, entry.text)]
    return [*texts, *card.notes]


def _has_card(tool: str, result: dict) -> bool:
    # An ambiguous or unknown merchant is asked back or answered by a fixed text: there is no figure to show.
    if tool == "get_merchant_spending":
        return result["match"]["status"] == "Matched"
    if tool in ("get_income_share", "get_spending_advice"):
        # K8-A: without a salary there is no share to show; the text answer says so. One currency at a time.
        return True
    return tool != "simulate_savings" or _matched(result)


def _matched(result: dict) -> bool:
    # The budget and the saving name a merchant only when asked for one; an unmatched one is answered by a fixed text.
    match = result["merchantMatch"]
    return match is None or match["status"] == "Matched"


def _target(result: dict) -> str | None:
    # "Market", "Starbucks", or None for all spending.
    if result["categoryDisplayName"]:
        return result["categoryDisplayName"]
    return result["merchantMatch"]["merchant"] if result["merchantMatch"] else None


def _against(amount: Decimal, budget: Decimal) -> Decimal:
    # A bar against the budget, full at the budget and never longer.
    return min(_share(amount, budget), Decimal("1.00"))


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


def _merchants(arguments: dict, result: dict, row: dict, suffix: str) -> Card:
    currency, merchants = row["currencyCode"], row["merchants"][:FACT_ROWS]
    bars = _bars([(m["displayName"], m["amount"], f"{money(m['amount'], currency)} · {percent(m['percentage'])}", None)
                  for m in merchants])
    sentence = f"En büyük pay {merchants[0]['displayName']}: {percent(merchants[0]['percentage'])}."
    return Card(f"İşyerleri · {_label(result['period'])}{suffix}", _headline(row["totalAmount"], currency), None,
                _part(bars[:SIMPLE_ROWS]), _part(bars[SIMPLE_ROWS:]), sentence)


def _highest(arguments: dict, result: dict, row: dict, suffix: str) -> Card:
    items = row["items"][:FACT_ROWS]
    entries = [Entry(i["merchant"] or UNCLASSIFIED, " · ".join(filter(None, [i["categoryDisplayName"], _day(i["localDate"])])),
                     money(i["amount"], i["currencyCode"])) for i in items]
    first = items[0]
    sentence = f"En büyüğü {entries[0].label}: {money(first['amount'], first['currencyCode'])}."
    return Card(f"En büyük harcamalar · {_label(result['period'])}{suffix}", _headline(first["amount"], first["currencyCode"]), None,
                _list(entries[:SIMPLE_ROWS]), _list(entries[SIMPLE_ROWS:]), sentence)


def _recurring(arguments: dict, result: dict) -> list[Card]:
    # The facts list the first five payments of all currencies together; a card shows its own currency's among them.
    shown, totals = result["payments"][:FACT_ROWS], result["monthlyTotals"]
    title = f"Düzenli ödemeler · {_label(result['period'])}"
    cards = []
    for total in totals:
        currency = total["currencyCode"]
        entries = [Entry(p["merchant"], f"{p['categoryDisplayName']} · sonraki {_day(p['pattern']['nextExpectedDate'])}",
                         f"{money(p['pattern']['latestAmount'], currency)} / ay") for p in shown if p["currencyCode"] == currency]
        amount = money(total["monthlyTotal"], currency)
        cards.append(Card(f"{title}{_suffix(total, totals)}", _headline(total["monthlyTotal"], currency), None,
                          _list(entries[:SIMPLE_ROWS]), _list(entries[SIMPLE_ROWS:]), f"Aylık toplam {amount}.", (RECURRING_RULE,)))
    return cards if result["payments"] else []


def _upcoming(arguments: dict, result: dict) -> list[Card]:
    shown, totals = result["payments"][:FACT_ROWS], result["totals"]
    title = f"Yaklaşan ödemeler · {period(date.fromisoformat(result['from']), date.fromisoformat(result['to']))}"
    cards = []
    for total in totals:
        currency = total["currencyCode"]
        entries = [Entry(p["merchant"], f"{_day(p['estimatedDate'])} · {_when(p['daysUntil'], p['isOverdue'])}",
                         money(p["estimatedAmount"], currency)) for p in shown if p["currencyCode"] == currency]
        amount = money(total["estimatedTotal"], currency)
        cards.append(Card(f"{title}{_suffix(total, totals)}", _headline(total["estimatedTotal"], currency), None,
                          _list(entries[:SIMPLE_ROWS]), _list(entries[SIMPLE_ROWS:]), f"Tahmini toplam {amount}.", (UPCOMING_RULE,)))
    return cards if result["payments"] else []


def _budget(arguments: dict, result: dict) -> list[Card]:
    if not _matched(result):
        return []
    f, currency, budget = result["figures"], result["currencyCode"], Decimal(str(result["budgetAmount"]))
    spent, projected = money(f["spentAmount"], currency), money(f["projectedMonthEnd"], currency)
    # K6-C: today and the month end, each against the budget; the bar is drawn, its length is never written.
    bars = (Bar("Bugün", f["spentAmount"], f"{spent} / {money(budget, currency)}", _against(f["spentAmount"], budget)),
            Bar("Bu hızla ay sonu", f["projectedMonthEnd"], projected, _against(f["projectedMonthEnd"], budget),
                "up" if f["willExceed"] else None))
    items = [Stat("Bütçe aşımı", money(f["overAmount"], currency)) if f["isOverBudget"]
             else Stat("Kalan", money(f["remainingAmount"], currency))]
    if f["willExceed"]:
        items.append(Stat("Tahmini aşım", money(f["projectedOverAmount"], currency)))
    detailed = (Block("stats", items=(Stat("Günlük ortalama", money(f["dailyAverage"], currency)), Stat("İşlem", str(f["transactionCount"])),
                                      Stat("Gün", f"ayın {f['elapsedDays']}. günü; ay {f['daysInMonth']} gün"))),)
    later = money(f["projectedOverAmount"], currency)
    if f["isOverBudget"]:
        sentence = f"Bütçeyi şimdiden {money(f['overAmount'], currency)} aştınız; bu hızla ay sonunda {later} aşar."
    else:
        sentence = f"Bu hızla bütçeyi {later} aşar." if f["willExceed"] else "Bu hızla bütçenin içinde kalır."
    target = _target(result)
    title = f"{target} bütçesi" if target else "Bütçe"
    return [Card(f"{title} · {_label(result['period'])}", _headline(f["spentAmount"], currency), None,
                 (Block("bars", rows=bars), Block("stats", items=tuple(items))), detailed, sentence, (BUDGET_RULE,))]


def _saving(arguments: dict, result: dict, row: dict, suffix: str) -> Card:
    currency, cut = row["currencyCode"], f"%{result['reductionPercent']}"
    monthly, annual = money(row["monthlySaving"], currency), money(row["annualSaving"], currency)
    target = _target(result)
    title = f"{target} tasarrufu {cut}" if target else f"Tasarruf {cut}"
    used = period(date.fromisoformat(result["from"]), date.fromisoformat(result["to"]))
    months = [{**m, "isPartial": False} for m in row["monthlyAmounts"]]
    detailed = (Block("series", points=_points(months, currency)),
                Block("stats", items=(Stat("Toplam harcama", money(row["totalSpent"], currency)), Stat("Dönem", f"{result['months']} tam ay"))))
    return Card(f"{title} · {used}{suffix}", _headline(row["monthlySaving"], currency), None,
                (Block("stats", items=(Stat("Yılda", annual), Stat("Aylık ortalama", money(row["monthlyAverage"], currency)))),),
                detailed, f"{cut} azaltırsanız ayda {monthly}, yılda {annual}.", (SAVINGS_RULE,))


def _income(arguments: dict, result: dict, row: dict, suffix: str) -> Card:
    currency, categories = row["currencyCode"], row["categories"][:FACT_ROWS]
    share = percent(row["spentShare"])
    bars = _bars([(c["displayName"], c["amount"], f"{money(c['amount'], currency)} · {percent(c['incomeShare'])}", None)
                  for c in categories])
    window = period(date.fromisoformat(result["incomeFrom"]), date.fromisoformat(result["incomeTo"]))
    simple = (Block("stats", items=(Stat("Harcama", money(row["totalSpent"], currency)),
                                    Stat("Gelir tabanı", money(row["incomeBase"], currency)))), *_part(bars[:SIMPLE_ROWS]))
    detailed = (*_part(bars[SIMPLE_ROWS:]), Block("stats", items=(Stat("Gelir dönemi", f"son 3 tam ayın ortalama maaşı: {window}"),)))
    # K7-B: the share is the answer to "maaşımın ne kadarını", so it is the big figure; it is not money.
    return Card(f"Gelir payı · {_label(result['period'])}{suffix}", Headline(None, None, share), None, simple, detailed,
                f"Gelirinizin {share} kadarını harcadınız.")


def _advice(arguments: dict, result: dict, row: dict, suffix: str) -> Card:
    currency, groups = row["currencyCode"], row["groups"]
    bars = _bars([(GROUP_LABELS[g["group"]], g["amount"], f"{money(g['amount'], currency)} · gelirin {percent(g['incomeShare'])}", None)
                  for g in groups])
    free = next((g for g in groups if g["group"] == "Discretionary"), None)
    sentence = (f"İsteğe bağlı harcama gelirin {percent(free['incomeShare'])} kadarı." if free
                else f"Toplam harcama {money(row['totalSpent'], currency)}.")
    # K18-C: the backend's own sentences as written, or why there is none; P3-A: always shown.
    limit = result["thresholds"]["discretionaryLimitPercent"]
    notes = tuple(a["message"] for a in row["advice"]) or (
        f"Yok: isteğe bağlı harcama gelirin %{limit} sınırının altında ve hiçbir kategori olağan ayından belirgin ölçüde sapmadı.",)
    return Card(f"Harcama önerisi · {_label(result['period'])}{suffix}", _headline(row["totalSpent"], currency), None, _part(bars), (),
                sentence, notes)


def _one_off(arguments: dict, result: dict) -> list[Card]:
    if not result["assets"]:
        return []
    currency, name = result["currencyCode"], result["categoryDisplayName"]
    title = " · ".join(filter(None, ["Ne olurdu", name, _label(result["period"])]))
    buy, value = _day(result["buyDate"]), _day(result["valueDate"])
    detailed = (Block("stats", items=(Stat("Alış günü", buy), Stat("Değerleme günü", value))),)
    return [Card(title, _headline(result["spentAmount"], currency), None, _list(_assets(result["assets"], currency)), detailed,
                 f"Alış {buy}, değerleme {value}.", _warnings(result))]


def _monthly(arguments: dict, result: dict) -> list[Card]:
    if result["adviceStatus"] == "NoAdvice" or not result["assets"]:
        return []
    currency = result["currencyCode"]
    buys = f"{result['months']} ay, her ayın 1'i: {_day(result['firstBuyDate'])} - {_day(result['lastBuyDate'])}"
    risks = [Stat(f.label, f.text) for a in result["assets"] if a["value"] is not None and a.get("volatility")
             for f in volatility_facts(a, currency)]
    invested = money(result["invested"], currency)
    return [Card(f"Aylık birikim · {money(result['amount'], currency)} × {result['months']} ay", _headline(Decimal(str(result["invested"])), currency),
                 None, _list(_assets(result["assets"], currency)), (Block("stats", items=(Stat("Alımlar", buys), *risks)),),
                 f"Toplam {invested} yatırmış olurdunuz; değerleme {_day(result['valueDate'])}.", _warnings(result))]


def _assets(assets: list[dict], currency: str) -> list[Entry]:
    # K8-C: an asset without a price stays as "hesaplanamadı"; hidden, the others would read as the only choices.
    return [Entry(asset_name(a), NO_PRICE.split(";")[0], "hesaplanamadı") if a["value"] is None else
            Entry(asset_name(a), f"{change_text(a['difference'], currency)} · {percent(abs(a['differencePercent']))}",
                  money(a["value"], currency), _direction(a["difference"]))
            for a in assets]


def _warnings(result: dict) -> tuple[str, ...]:
    # INC-03 K6-A: the investment warning is always under a what-if; alternatives are never a sum.
    return (result["disclaimer"], *alternatives_line(result["assets"]))


_BUILDERS = {
    "get_spending_summary": _summary,
    "get_merchant_breakdown": _merchants,
    "get_highest_transactions": _highest,
    "get_category_breakdown": _categories,
    "get_category_spending": _category_spending,
    "get_merchant_spending": _merchant,
    "compare_spending": _comparison,
    "get_monthly_trend": _trend,
    "simulate_savings": _saving,
    "get_income_share": _income,
    "get_spending_advice": _advice,
}

# Results that are not one row per currency: the builder makes its own cards, one per currency of its totals.
_WHOLE_BUILDERS = {
    "get_recurring_payments": _recurring,
    "get_upcoming_payments": _upcoming,
    "check_budget": _budget,
    "what_if_one_off": _one_off,
    "what_if_monthly": _monthly,
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


def _list(entries: list[Entry]) -> tuple[Block, ...]:
    return (Block("list", entries=tuple(entries)),) if entries else ()


def _day(text: str) -> str:
    return day(date.fromisoformat(text))


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
