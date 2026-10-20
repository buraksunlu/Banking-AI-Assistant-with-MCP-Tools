from datetime import date
from decimal import Decimal

import pytest

from formatting import day, money, month, percent, period


@pytest.mark.parametrize(
    ("amount", "currency", "text"),
    [
        ("735.00", "TRY", "735,00 TL"),
        ("735", "TRY", "735,00 TL"),
        ("12345.67", "TRY", "12.345,67 TL"),
        ("1359.98", "TRY", "1.359,98 TL"),
        ("1234567.5", "TRY", "1.234.567,50 TL"),
        ("0.5", "TRY", "0,50 TL"),
        ("-415.00", "TRY", "-415,00 TL"),
        ("120.00", "USD", "120,00 USD"),
        ("1000", "EUR", "1.000,00 EUR"),
    ],
)
def test_money_is_written_the_turkish_way(amount, currency, text):
    assert money(Decimal(amount), currency) == text


def test_money_never_rounds_silently():
    # The backend sends two decimals; a third would mean something upstream is wrong, so it is not hidden.
    with pytest.raises(ValueError):
        money(Decimal("1.005"), "TRY")


@pytest.mark.parametrize(
    ("value", "text"),
    [("80.27", "%80,27"), ("19.73", "%19,73"), ("100", "%100,00"), ("0", "%0,00"), ("-27.14", "-%27,14")],
)
def test_percent(value, text):
    assert percent(Decimal(value)) == text


def test_every_month_has_its_turkish_name():
    names = [day(date(2026, month, 1)) for month in range(1, 13)]
    assert names == ["1 Ocak 2026", "1 Şubat 2026", "1 Mart 2026", "1 Nisan 2026", "1 Mayıs 2026", "1 Haziran 2026",
                     "1 Temmuz 2026", "1 Ağustos 2026", "1 Eylül 2026", "1 Ekim 2026", "1 Kasım 2026", "1 Aralık 2026"]


def test_a_month_is_its_turkish_name_and_year():
    assert [month(2026, 7), month(2026, 10), month(2027, 1)] == ["Temmuz 2026", "Ekim 2026", "Ocak 2027"]


@pytest.mark.parametrize(
    ("start", "end", "text"),
    [
        (date(2026, 10, 9), date(2026, 10, 9), "9 Ekim 2026"),
        (date(2026, 10, 1), date(2026, 10, 10), "1-10 Ekim 2026"),
        (date(2026, 9, 28), date(2026, 10, 4), "28 Eylül - 4 Ekim 2026"),
        (date(2026, 12, 28), date(2027, 1, 3), "28 Aralık 2026 - 3 Ocak 2027"),
        (date(2026, 1, 1), date(2026, 10, 10), "1 Ocak - 10 Ekim 2026"),
    ],
)
def test_period(start, end, text):
    assert period(start, end) == text


def test_money_accepts_whole_numbers_but_never_float():
    # JSON may carry a whole amount as an integer; a float may already have lost the exact amount.
    assert money(590, "TRY") == "590,00 TL"
    with pytest.raises(TypeError):
        money(0.1, "TRY")
