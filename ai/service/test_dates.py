from datetime import date

import pytest

from dates import CustomerDay, read_dates, with_span_replaced

# The demo moment, a Saturday: every expected date below is worked out by hand from it (CHAT-08 K3-D1, K4-A).
TODAY = date(2026, 10, 10)


def found(message: str) -> tuple[date, date]:
    reading = read_dates(message, TODAY)
    assert reading.kind == "found", (message, reading)
    return reading.start, reading.end


@pytest.mark.parametrize(
    ("message", "start", "end"),
    [
        # One day.
        ("1 Eylül'de ne kadar harcadım?", date(2026, 9, 1), date(2026, 9, 1)),
        ("1 Eylül ne harcadım", date(2026, 9, 1), date(2026, 9, 1)),
        ("1 EYLÜL", date(2026, 9, 1), date(2026, 9, 1)),
        ("1 eylul", date(2026, 9, 1), date(2026, 9, 1)),
        ("1 Eylül 2025'te ne kadar harcadım?", date(2025, 9, 1), date(2025, 9, 1)),
        ("01.09.2025 ne harcadım", date(2025, 9, 1), date(2025, 9, 1)),
        # K4-A: no year means the most recent past; today itself counts.
        ("10 Ekim'de ne harcadım?", date(2026, 10, 10), date(2026, 10, 10)),
        ("11 Ekim'de ne harcadım?", date(2025, 10, 11), date(2025, 10, 11)),
        ("15 Kasım", date(2025, 11, 15), date(2025, 11, 15)),
        ("29 Şubat", date(2024, 2, 29), date(2024, 2, 29)),
        # A range in one month.
        ("1-5 Eylül arası ne kadar harcadım?", date(2026, 9, 1), date(2026, 9, 5)),
        ("1 - 5 Eylül", date(2026, 9, 1), date(2026, 9, 5)),
        ("1–5 Eylül", date(2026, 9, 1), date(2026, 9, 5)),
        ("1 ile 5 Eylül arası", date(2026, 9, 1), date(2026, 9, 5)),
        ("1-5 Eylül 2025", date(2025, 9, 1), date(2025, 9, 5)),
        # A range over two months.
        ("1 Eylül - 5 Eylül", date(2026, 9, 1), date(2026, 9, 5)),
        ("1 Eylül ile 5 Eylül arası ne harcadım?", date(2026, 9, 1), date(2026, 9, 5)),
        ("1 Eylül'den 5 Eylül'e kadar", date(2026, 9, 1), date(2026, 9, 5)),
        ("28 Ağustos - 3 Eylül", date(2026, 8, 28), date(2026, 9, 3)),
        ("28 Aralık - 3 Ocak", date(2025, 12, 28), date(2026, 1, 3)),
        ("28 Aralık 2025 - 3 Ocak 2026", date(2025, 12, 28), date(2026, 1, 3)),
        ("01.09.2026 - 05.09.2026", date(2026, 9, 1), date(2026, 9, 5)),
        ("01.09.2026 ile 05.09.2026 arası", date(2026, 9, 1), date(2026, 9, 5)),
        # A whole month; the running month ends today.
        ("Eylül'de ne harcadım?", date(2026, 9, 1), date(2026, 9, 30)),
        ("Eylül ayında ne harcadım?", date(2026, 9, 1), date(2026, 9, 30)),
        ("Ekim'de ne harcadım?", date(2026, 10, 1), date(2026, 10, 10)),
        ("Kasım'da ne harcadım?", date(2025, 11, 1), date(2025, 11, 30)),
        ("Şubat 2024'te ne harcadım?", date(2024, 2, 1), date(2024, 2, 29)),
        ("Mart'ta market harcaması", date(2026, 3, 1), date(2026, 3, 31)),
    ],
)
def test_a_known_pattern_is_read_without_a_model(message, start, end):
    assert found(message) == (start, end)


@pytest.mark.parametrize(
    ("message", "reason"),
    [
        ("31 Şubat'ta ne harcadım?", "Şubat diye bir gün yok"),
        ("31 Eylül", "Eylül diye bir gün yok"),
        ("1 Kasım 2026'da ne harcadım?", "henüz gelmedi"),
        ("Kasım 2026'da ne harcadım?", "henüz gelmedi"),
        ("5-1 Eylül arası", "Bitiş tarihi başlangıçtan önce"),
        ("5 Eylül - 1 Eylül 2026", "Bitiş tarihi başlangıçtan önce"),
        ("1 Ocak 2024 - 5 Ekim 2026", "24 ay"),
        ("30.02.2026", "diye bir gün yok"),
    ],
)
def test_an_impossible_date_is_invalid_with_a_reason_for_the_user(message, reason):
    reading = read_dates(message, TODAY)

    assert reading.kind == "invalid"
    assert reason in reading.reason


@pytest.mark.parametrize(
    "message",
    ["Geçen salı ne harcadım?", "3 gün önce ne harcadım?", "Geçen yıl ne harcadım?", "Bayramda ne harcadım?",
     "Hafta sonu ne harcadım?", "Evvelsi gün ne harcadım?", "03/09 ne harcadım?", "Cuma günü ne harcadım?"],
)
def test_a_date_that_code_cannot_read_is_left_for_the_second_step(message):
    assert read_dates(message, TODAY).kind == "unreadable"


@pytest.mark.parametrize(
    "message",
    ["Ne kadar harcadım?", "Bu ay ne kadar harcadım?", "Geçen ay ne harcadım?", "Son 30 günde ne harcadım?", "Dün ne harcadım?",
     "Market'te ne harcadım?", "Marketlere ne kadar verdim?", "Bütçem 1.500 TL, ne durumda?", "Son 3 ayda Starbucks?",
     "Yaklaşan ödemelerim neler?", "Kaç ayda biriktiririm?", "Aylık sabit giderlerim?"],
)
def test_a_message_without_a_date_is_none(message):
    assert read_dates(message, TODAY).kind == "none"


def test_the_leap_day_of_a_leap_year_is_a_day():
    assert found("29 Şubat 2024") == (date(2024, 2, 29), date(2024, 2, 29))
    assert read_dates("29 Şubat 2025", TODAY).kind == "invalid"


def test_the_span_covers_the_dates_words_and_their_endings():
    message = "Migros'a 1-5 Eylül arası ne verdim?"
    reading = read_dates(message, TODAY)

    assert message[slice(*reading.span)] == "1-5 Eylül arası"
    assert with_span_replaced(message, reading.span, "bu ay") == "Migros'a bu ay ne verdim?"


@pytest.mark.parametrize(
    ("message", "rewritten"),
    [
        ("1 Eylül'de ne kadar harcadım?", "bu ay ne kadar harcadım?"),
        ("Eylül ayında Starbucks'a ne verdim?", "bu ay Starbucks'a ne verdim?"),
        ("1 Eylül'den 5 Eylül'e kadar en büyük harcamam?", "bu ay en büyük harcamam?"),
        ("Peki 6-10 Eylül?", "Peki bu ay?"),
    ],
)
def test_the_date_is_replaced_by_a_period_name_the_model_was_measured_with(message, rewritten):
    reading = read_dates(message, TODAY)

    assert with_span_replaced(message, reading.span, "bu ay") == rewritten


def test_the_customer_day_holds_the_calendar_the_api_sent():
    day = CustomerDay(TODAY, "Europe/Istanbul")

    assert (day.today, day.time_zone) == (TODAY, "Europe/Istanbul")


# --- CHAT-08 PR3: what the second step is given ----------------------------------------------------------------------------

from dates import check_range  # noqa: E402


@pytest.mark.parametrize(
    "message",
    ["Eylül'ün ilk haftası ne harcadım?", "Eylül'ün son haftasında ne harcadım?", "Eylül başında ne harcadım?",
     "Ekim sonu ne harcadım?", "Kasım ortasında ne harcadım?"],
)
def test_a_part_of_a_month_is_not_the_whole_month(message):
    # Read as "Eylül" it would answer for thirty days when the user asked for seven.
    assert read_dates(message, TODAY).kind == "unreadable"


def test_since_the_new_year_is_one_expression():
    message = "Yılbaşından beri ne harcadım?"
    reading = read_dates(message, TODAY)

    assert (reading.kind, message[slice(*reading.span)]) == ("unreadable", "Yılbaşından beri")


@pytest.mark.parametrize(
    ("start", "end", "kind"),
    [
        ("2026-10-06", "2026-10-06", "found"),
        ("2025-01-01", "2025-12-31", "found"),
        ("2026-10-10", "2026-10-10", "found"),
        ("2026-10-11", "2026-10-11", "invalid"),
        ("2026-10-06", "2026-10-05", "invalid"),
        ("2024-01-01", "2026-10-05", "invalid"),
        ("2026-02-30", "2026-03-01", "invalid"),
        ("06.10.2026", "2026-10-06", "invalid"),
        ("2026-10-06", "", "invalid"),
        ("", "", "invalid"),
    ],
)
def test_a_range_the_model_wrote_is_checked_like_one_the_user_wrote(start, end, kind):
    assert check_range(start, end, TODAY).kind == kind
