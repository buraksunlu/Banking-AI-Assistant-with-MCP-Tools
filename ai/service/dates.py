"""The customer's own calendar, and the dates the user writes (CHAT-08).

"Today" and the time zone come from the .NET API with every chat message (K9-C), never from this service's own clock: a date read
from the user's words must mean what the analyses mean by it, and the demo moment is fixed on the .NET side only.

``read_dates`` reads the patterns people use for a day, a range of days or a whole month, in code and without a model (K3-D1);
the model never chooses a date (ANA-20). Every expression with a fixed calendar rule is read here too (CHAT-14 K1-C): a part of a
month ("Eylül'ün ilk haftası", "Ekim sonu"), "yılbaşından beri", "geçen yıl", "3 gün önce", "evvelsi gün", "geçen salı", "geçen
hafta sonu", "2 hafta önce": the model gave different ranges for the same words at temperature 0, one of them a week of the wrong
month. A message that looks like a date but has no such rule ("salı günü", "bayramda", "03/09") is ``unreadable``: it needs the
second step (K1-C), whose range ``fits_words`` holds to the month, weekday and year written. An impossible date ("31 Şubat", the future, more than 24
months) is ``invalid`` with a reason written for the user (K5-B). A year the user did not write is the most recent past one
(K4-A). Standard library only.
"""

import re
from dataclasses import dataclass, replace
from datetime import date, timedelta

from answer_check import fold_turkish
from formatting import MONTH_NAMES, period

# The backend's own limit (PeriodResolver.MaximumCustomMonths); checked here too so the user is told why, in words.
MAXIMUM_MONTHS = 24


@dataclass(frozen=True)
class CustomerDay:
    """The customer's today and the id of their time zone ("Europe/Istanbul")."""

    today: date
    time_zone: str


@dataclass(frozen=True)
class DateReading:
    """What the message says about dates: ``kind`` is "none", "found" (``start``..``end``, inclusive), "invalid" (``reason``) or
    "unreadable"; ``span`` is where the date words are in the message (character positions), for "found", "invalid" and "unreadable"."""

    kind: str
    start: date | None = None
    end: date | None = None
    reason: str = ""
    span: tuple[int, int] | None = None


_NONE = DateReading("none")

# The patterns read folded text (answer_check.fold_turkish: every Turkish letter plain, CHAT-09 K11-A): people type both "Eylül"
# and "eylul". The length does not change, so a position in the folded text is the same position in the message.

_MONTH = "ocak|subat|mart|nisan|mayis|haziran|temmuz|agustos|eylul|ekim|kasim|aralik"
_MONTH_NUMBER = {name: number for number, name in enumerate(_MONTH.split("|"), start=1)}
_ENDING = r"(?:['’]?(?:ayindan|ayinda|ayina|ayi|de|da|te|ta|den|dan|ten|tan|e|a))?(?![a-z])"
# "Eylül ayında": the word "ay" after the month belongs to the date.
_AY = r"(?:\s+ay(?:inda|indan|ina|i)?(?![a-z]))?"
_YEAR = r"(?:\s+(?P<{y}>20\d\d)(?:['’]?(?:de|da|te|ta|den|dan|ten|tan|e|a|ye|ya))?(?![a-z0-9]))?"
_TAIL = r"(?:\s+(?:arasi|arasinda|boyunca|icinde|kadar))?"
_SEP_OPTIONAL = r"\s*(?:-|–|—|ile|ila|ve)?\s*"
_SEP = r"\s*(?:-|–|—|ile|ila)\s*"
_DAY = r"(?<![\d.,/])(?P<{d}>\d{{1,2}})(?!\d)"
_NUMERIC = r"(?<![\d.,])(?P<{d}>\d{{1,2}})\.(?P<{m}>\d{{1,2}})\.(?P<{y}>\d{{4}})(?!\d)"


def _day(n: str) -> str:
    return _DAY.format(d=f"d{n}")


def _year(n: str) -> str:
    return _YEAR.format(y=f"y{n}")


def _numeric(n: str) -> str:
    return _NUMERIC.format(d=f"d{n}", m=f"m{n}", y=f"y{n}")


_NUMERIC_RANGE = re.compile(_numeric("1") + _SEP_OPTIONAL + _numeric("2") + _TAIL)
_NUMERIC_DAY = re.compile(_numeric("1"))
_TWO_MONTHS = re.compile(
    _day("1") + rf"\s+(?P<m1>{_MONTH}){_ENDING}{_AY}" + _year("1") + _SEP_OPTIONAL + _day("2")
    + rf"\s+(?P<m2>{_MONTH}){_ENDING}{_AY}" + _year("2") + _TAIL)
_ONE_MONTH_RANGE = re.compile(
    _day("1") + _SEP + _day("2") + rf"\s+(?P<m>{_MONTH}){_ENDING}{_AY}" + _year("") + _TAIL)
_ONE_DAY = re.compile(_day("1") + rf"\s+(?P<m>{_MONTH}){_ENDING}{_AY}" + _year(""))
_WHOLE_MONTH = re.compile(rf"(?<![a-z0-9])(?P<m>{_MONTH}){_ENDING}{_AY}" + _year(""))

# A part of a month ("Eylül'ün ilk haftası", "Ekim sonu") is not the month: read before the whole month, never as thirty days.
_MONTH_PART = re.compile(
    rf"(?<![a-z0-9])(?P<m>{_MONTH})(?:\s+(?P<y>20\d\d))?['’]?[a-z]*(?:\s+ayi[a-z]*)?\s+"
    r"(?:(?P<nth>ilk|birinci|ikinci|ucuncu|dorduncu|son|[1-4]\.)\s*hafta[a-z]*|(?P<part>basi|ortasi|sonu)[a-z]*)")
_NTH_WEEK = {"ilk": 1, "birinci": 1, "ikinci": 2, "ucuncu": 3, "dorduncu": 4, "1.": 1, "2.": 2, "3.": 3, "4.": 4}
# K2-B: fixed thirds of a month; the "Dönem:" line of the answer shows the user the days that were taken.
_PART_DAYS = {"basi": (1, 10), "ortasi": (11, 20), "sonu": (21, 31)}

# The weekday names the second step's calendar is written with (date.weekday(): Monday is 0).
WEEKDAYS = ("Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi", "Pazar")
# Folded, longest first: "cumartesi" before "cuma", "pazartesi" before "pazar".
_WEEKDAY = "pazartesi|cumartesi|carsamba|persembe|pazar|sali|cuma"
_WEEKDAY_NUMBER = {"pazartesi": 0, "sali": 1, "carsamba": 2, "persembe": 3, "cuma": 4, "cumartesi": 5, "pazar": 6}
_WEEKDAY_ENDING = r"(?:ya|ye|yi|da|de|dan|den|si|ki)?(?:\s+gun[a-z]*)?(?![a-z])"

_SINCE_NEW_YEAR = re.compile(r"(?<![a-z])yilbasindan\s+(?:beri|bu\s+yana|itibaren|bugune\s+kadar)(?![a-z])")
_LAST_YEAR = re.compile(r"(?<![a-z])gecen\s+(?:yil|sene)(?:ki|in|da|de|a|e)?(?![a-z])")
_AGO = re.compile(r"(?<![\d.,])(?P<n>\d{1,2})\s*(?P<unit>gun|hafta|ay|yil|sene)\s+once(?:ki|si)?(?![a-z])")
_DAY_BEFORE_YESTERDAY = re.compile(r"(?<![a-z])(?:evvelsi|evvelki)\s+gun[a-z]*")
_LAST_WEEKEND = re.compile(r"(?<![a-z])gecen\s+hafta\s*sonu[a-z]*")
_LAST_WEEKDAY = re.compile(rf"(?<![a-z])gecen\s+(?P<wd>{_WEEKDAY}){_WEEKDAY_ENDING}")

# Looks like a date, is not a pattern above: weekdays, "N days ago", "last year", holidays, slash and short forms.
_UNREADABLE = re.compile(
    rf"(?<![a-z])(?:gecen\s+)?(?:{_WEEKDAY}){_WEEKDAY_ENDING}"
    r"|(?<![a-z])(?:evvelsi|yilbasi[a-z]*(?:\s+beri)?|bayram[a-z]*|hafta\s*sonu[a-z]*|gecen\s+(?:yil|sene)|"
    r"\d+\s*(?:gun|hafta|ay|yil|sene)\s+once)(?![a-z])"
    r"|(?<![\d.,/])\d{1,2}/\d{1,2}(?:/\d{2,4})?(?![\d/])"
    r"|(?<![\d.,])\d{1,2}\.\d{1,2}\.\d{2}(?!\d)")


def read_dates(message: str, today: date) -> DateReading:
    """The dates ``message`` writes, relative to the customer's ``today``; see the module description for the four kinds."""
    text = fold_turkish(message)
    for pattern, build in ((_MONTH_PART, _month_part), (_NUMERIC_RANGE, _numeric_range), (_NUMERIC_DAY, _numeric_day),
                           (_TWO_MONTHS, _two_months), (_ONE_MONTH_RANGE, _one_month_range), (_ONE_DAY, _one_day),
                           (_WHOLE_MONTH, _whole_month), (_SINCE_NEW_YEAR, _since_new_year), (_LAST_YEAR, _last_year),
                           (_AGO, _ago), (_DAY_BEFORE_YESTERDAY, _day_before_yesterday), (_LAST_WEEKEND, _last_weekend),
                           (_LAST_WEEKDAY, _last_weekday)):
        match = pattern.search(text)
        if match:
            return replace(build(match.groupdict(), today), span=match.span())
    unreadable = _UNREADABLE.search(text)
    return DateReading("unreadable", span=unreadable.span()) if unreadable else _NONE


def check_range(start: str, end: str, today: date) -> DateReading:
    """A range written as two ``YYYY-MM-DD`` texts (what the second step's model answers) under the rules of a range the user
    wrote: real days, start not after end, not in the future, at most 24 months; "found" or "invalid" with a reason."""
    try:
        days = [date.fromisoformat(text) for text in (start, end) if re.fullmatch(r"\d{4}-\d{2}-\d{2}", text)]
    except ValueError:
        return _invalid("Bu tarih geçerli bir gün değil.")
    if len(days) != 2:
        return _invalid("Bu tarih geçerli bir gün değil.")
    return _checked(days[0], days[1], today)


def period_line(arguments: dict) -> str | None:
    """"Dönem: 1-5 Eylül 2026." for a call that ran a range of days (K16-A), fixed by code after the answer check; None for any
    other call."""
    if arguments.get("period") != "Custom":
        return None
    return f"Dönem: {period(date.fromisoformat(arguments['from']), date.fromisoformat(arguments['to']))}."


def with_span_replaced(message: str, span: tuple[int, int], replacement: str) -> str:
    """``message`` with the characters of ``span`` replaced; the other words stay as the user wrote them."""
    return message[:span[0]] + replacement + message[span[1]:]


def _invalid(reason: str) -> DateReading:
    return DateReading("invalid", reason=reason)


def _checked(start: date, end: date, today: date) -> DateReading:
    if start > end:
        return _invalid("Bitiş tarihi başlangıçtan önce olamaz.")
    if end > today:
        return _invalid("Bu tarih henüz gelmedi.")
    if start < _add_months(end, -MAXIMUM_MONTHS):
        return _invalid(f"En fazla {MAXIMUM_MONTHS} aylık bir dönemi sorabilirsiniz.")
    return DateReading("found", start, end)


def _add_months(day: date, months: int) -> date:
    # Like DateOnly.AddMonths: a short month clamps the day (31 May - 3 months = 28 February).
    index = day.year * 12 + day.month - 1 + months
    year, month = divmod(index, 12)
    month += 1
    last = (date(year + (month == 12), month % 12 + 1, 1) - timedelta(days=1)).day
    return date(year, month, min(day.day, last))


def _nonexistent(d: int, m: int) -> DateReading:
    return _invalid(f"{d} {MONTH_NAMES[m - 1]} diye bir gün yok.")


def _recent(d: int, m: int, today: date, not_after: date | None = None) -> date | None:
    """The latest year's date of this day and month that is not after ``not_after`` (default: today); None when none exists."""
    limit = not_after or today
    for year in range(limit.year, limit.year - 9, -1):
        try:
            found = date(year, m, d)
        except ValueError:
            continue
        if found <= limit:
            return found
    return None


def _one_day(groups: dict, today: date) -> DateReading:
    d, m = int(groups["d1"]), _MONTH_NUMBER[groups["m"]]
    if groups["y"]:
        try:
            day = date(int(groups["y"]), m, d)
        except ValueError:
            return _nonexistent(d, m)
    else:
        day = _recent(d, m, today)
        if day is None:
            return _nonexistent(d, m)
    return _checked(day, day, today)


def _numeric_day(groups: dict, today: date) -> DateReading:
    d, m, y = int(groups["d1"]), int(groups["m1"]), int(groups["y1"])
    try:
        day = date(y, m, d)
    except ValueError:
        return _invalid(f"{d:02}.{m:02}.{y} diye bir gün yok.")
    return _checked(day, day, today)


def _numeric_range(groups: dict, today: date) -> DateReading:
    ends = []
    for n in "12":
        d, m, y = int(groups[f"d{n}"]), int(groups[f"m{n}"]), int(groups[f"y{n}"])
        try:
            ends.append(date(y, m, d))
        except ValueError:
            return _invalid(f"{d:02}.{m:02}.{y} diye bir gün yok.")
    return _checked(ends[0], ends[1], today)


def _one_month_range(groups: dict, today: date) -> DateReading:
    d1, d2, m = int(groups["d1"]), int(groups["d2"]), _MONTH_NUMBER[groups["m"]]
    if groups["y"]:
        year = int(groups["y"])
    else:
        # The same month on both sides: the year is that of the later day.
        end = _recent(d2, m, today)
        if end is None:
            return _nonexistent(d2, m)
        year = end.year
    try:
        return _checked(date(year, m, d1), date(year, m, d2), today)
    except ValueError:
        return _nonexistent(max(d1, d2), m)


def _two_months(groups: dict, today: date) -> DateReading:
    d1, m1 = int(groups["d1"]), _MONTH_NUMBER[groups["m1"]]
    d2, m2 = int(groups["d2"]), _MONTH_NUMBER[groups["m2"]]
    y1 = int(groups["y1"]) if groups["y1"] else None
    y2 = int(groups["y2"]) if groups["y2"] else None
    try:
        if y2 is not None:
            end = date(y2, m2, d2)
        elif y1 is not None:
            # "28 Aralık 2025 - 3 Ocak": the end is the first such day not before the start.
            end = date(y1 + ((m2, d2) < (m1, d1)), m2, d2)
        else:
            end = _recent(d2, m2, today)
            if end is None:
                return _nonexistent(d2, m2)
        if y1 is not None:
            start = date(y1, m1, d1)
        elif (m1, d1) <= (m2, d2):
            start = date(end.year, m1, d1)
        elif m1 > m2:
            # Over the turn of the year ("28 Aralık - 3 Ocak"): the start is in the year before the end's.
            start = date(end.year - 1, m1, d1)
        else:
            return _invalid("Bitiş tarihi başlangıçtan önce olamaz.")
    except ValueError:
        return _nonexistent(max(d1, d2), m1 if d1 >= d2 else m2)
    return _checked(start, end, today)


def _whole_month(groups: dict, today: date) -> DateReading:
    m = _MONTH_NUMBER[groups["m"]]
    year = int(groups["y"]) if groups["y"] else (today.year if m <= today.month else today.year - 1)
    start = date(year, m, 1)
    if start > today:
        return _invalid("Bu tarih henüz gelmedi.")
    last = _add_months(start, 1) - timedelta(days=1)
    # The running month ends today: the days to come have no purchases to ask about.
    return _checked(start, min(last, today), today)



def _month_year(m: int, written: str | None, today: date) -> int:
    # As for a whole month (K4-A): a month after this one is last year's.
    return int(written) if written else (today.year if m <= today.month else today.year - 1)


def _month_part(groups: dict, today: date) -> DateReading:
    m = _MONTH_NUMBER[groups["m"]]
    first = date(_month_year(m, groups["y"], today), m, 1)
    last = (_add_months(first, 1) - timedelta(days=1)).day
    if groups["nth"] == "son":
        days = (last - 6, last)
    elif groups["nth"]:
        week = _NTH_WEEK[groups["nth"]]
        days = (7 * week - 6, 7 * week)
    else:
        start, end = _PART_DAYS[groups["part"]]
        days = (start, min(end, last))
    return _until_today(first.replace(day=days[0]), first.replace(day=days[1]), today)


def _until_today(start: date, end: date, today: date) -> DateReading:
    """A range that may still be running: it ends today, as the running month does; one that has not begun is not yet."""
    if start > today:
        return _invalid("Bu tarih henüz gelmedi.")
    return _checked(start, min(end, today), today)


def _since_new_year(groups: dict, today: date) -> DateReading:
    return _checked(date(today.year, 1, 1), today, today)


def _last_year(groups: dict, today: date) -> DateReading:
    return _checked(date(today.year - 1, 1, 1), date(today.year - 1, 12, 31), today)


def _ago(groups: dict, today: date) -> DateReading:
    """K4-A: the day, the Monday-Sunday week, the month or the year that holds the day N units back."""
    n, unit = int(groups["n"]), groups["unit"]
    if unit == "gun":
        day = today - timedelta(days=n)
        return _checked(day, day, today)
    if unit == "hafta":
        monday = today - timedelta(days=7 * n + today.weekday())
        return _until_today(monday, monday + timedelta(days=6), today)
    if unit == "ay":
        first = _add_months(today.replace(day=1), -n)
        return _until_today(first, _add_months(first, 1) - timedelta(days=1), today)
    year = today.year - n
    return _until_today(date(year, 1, 1), date(year, 12, 31), today)


def _day_before_yesterday(groups: dict, today: date) -> DateReading:
    day = today - timedelta(days=2)
    return _checked(day, day, today)


def _last_weekend(groups: dict, today: date) -> DateReading:
    # The weekend of the week before this one: on a Saturday or a Sunday, the one under way is "bu hafta sonu".
    monday = today - timedelta(days=today.weekday())
    return _checked(monday - timedelta(days=2), monday - timedelta(days=1), today)


def _last_weekday(groups: dict, today: date) -> DateReading:
    # The latest such day before today, never today: the rule the second step's table was written with (date-v4).
    back = (today.weekday() - _WEEKDAY_NUMBER[groups["wd"]] - 1) % 7 + 1
    day = today - timedelta(days=back)
    return _checked(day, day, today)


_MONTH_WORD = re.compile(rf"(?<![a-z0-9])({_MONTH})")
_WEEKDAY_WORD = re.compile(rf"(?<![a-z])({_WEEKDAY}){_WEEKDAY_ENDING}")
_YEAR_WORD = re.compile(r"(?<![\d.,/])(20\d\d)(?!\d)")


def fits_words(expression: str, start: date, end: date) -> bool:
    """Whether a range the model gave keeps to the month, weekday and year written in ``expression`` (CHAT-14 K1-C): check_range
    only knows that the days are real, so a week of September for "the second week of October" passed it."""
    text = fold_turkish(expression)
    months = {_MONTH_NUMBER[name] for name in _MONTH_WORD.findall(text)}
    if months and not {start.month, end.month} <= months:
        return False
    weekday = _WEEKDAY_WORD.search(text)
    if weekday and start.weekday() != _WEEKDAY_NUMBER[weekday.group(1)]:
        return False
    years = {int(year) for year in _YEAR_WORD.findall(text)}
    return not years or {start.year, end.year} <= years
