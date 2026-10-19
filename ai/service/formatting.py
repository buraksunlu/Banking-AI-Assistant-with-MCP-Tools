"""Turns backend values into the Turkish text the model is given (AI-09, for the AI-14a tools).

Deterministic and independent of the machine's locale: the model never formats, rounds or converts a number itself.
"""

from datetime import date
from decimal import Decimal

_MONTHS = ("Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran",
           "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık")

# How people write the currency in Turkish; the others keep their ISO code ("120,00 USD").
_CURRENCY_NAMES = {"TRY": "TL"}

_CENT = Decimal("0.01")


def money(amount: Decimal, currency: str) -> str:
    """735.00 TRY → "735,00 TL", 120 USD → "120,00 USD"; refuses more than two decimals instead of rounding."""
    return f"{_number(amount)} {_CURRENCY_NAMES.get(currency, currency)}"


def percent(value: Decimal) -> str:
    """80.27 → "%80,27", -27.14 → "-%27,14"."""
    # The sign goes before the percent sign, as in "-%27,14"; "%-27,14" reads as a typo.
    text = _number(abs(value))
    return f"-%{text}" if value < 0 else f"%{text}"


def day(value: date) -> str:
    """2026-10-10 → "10 Ekim 2026"."""
    return f"{value.day} {_MONTHS[value.month - 1]} {value.year}"


def period(start: date, end: date) -> str:
    """An inclusive range written the short Turkish way: "9 Ekim 2026", "1-10 Ekim 2026", "28 Eylül - 4 Ekim 2026"."""
    if start == end:
        return day(start)
    if start.year != end.year:
        return f"{day(start)} - {day(end)}"
    if start.month != end.month:
        return f"{start.day} {_MONTHS[start.month - 1]} - {day(end)}"
    return f"{start.day}-{day(end)}"


def _number(amount: Decimal | int) -> str:
    # A float may already have lost the exact amount (0.1 + 0.2 is not 0.3), so it is refused rather than formatted.
    if isinstance(amount, float):
        raise TypeError("Amounts must be Decimal or int, never float.")
    amount = Decimal(amount)
    # The backend stores numeric(18,2); a third decimal would mean something upstream is wrong, so it is not hidden.
    if amount != amount.quantize(_CENT):
        raise ValueError(f"Amount {amount} has more than two decimals.")
    # Format with "," for thousands and "." for decimals, then swap them; this never depends on the machine's locale.
    english = f"{amount.quantize(_CENT):,.2f}"
    return english.replace(",", "_").replace(".", ",").replace("_", ".")
