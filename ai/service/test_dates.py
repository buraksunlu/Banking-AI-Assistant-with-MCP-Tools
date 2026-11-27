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
    ["Bayramda ne harcadım?", "Hafta sonu ne harcadım?", "03/09 ne harcadım?", "Cuma günü ne harcadım?", "Salı günü ne harcadım?"],
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


# --- CHAT-14: what has a fixed rule is read by code, never by the model ----------------------------------------------------------

@pytest.mark.parametrize(
    ("message", "start", "end"),
    [
        # K3-A: the weeks of a month are its days (1-7, 8-14, 15-21, 22-28) and its last week its last seven days.
        ("Eylül'ün ilk haftası ne harcadım?", date(2026, 9, 1), date(2026, 9, 7)),
        ("eylulun ilk haftasi", date(2026, 9, 1), date(2026, 9, 7)),
        ("Eylül'ün son haftasında ne harcadım?", date(2026, 9, 24), date(2026, 9, 30)),
        ("Ağustosun son haftası ne harcadım?", date(2026, 8, 25), date(2026, 8, 31)),
        ("Şubat'ın son haftası", date(2026, 2, 22), date(2026, 2, 28)),
        ("Şubat 2024'ün son haftası", date(2024, 2, 23), date(2024, 2, 29)),
        ("Eylül ayının üçüncü haftası", date(2026, 9, 15), date(2026, 9, 21)),
        ("Ağustos'un dördüncü haftası", date(2026, 8, 22), date(2026, 8, 28)),
        # The running month ends today, as a whole month does: the second week of October is 8-10 October on the 10th.
        ("Ekimin ikinci haftası ne harcadım?", date(2026, 10, 8), date(2026, 10, 10)),
        ("Ekim'in ilk haftası", date(2026, 10, 1), date(2026, 10, 7)),
        # Written with a number: read as the whole month before (1-10 October for "the 2nd week").
        ("Ekim'in 2. haftası", date(2026, 10, 8), date(2026, 10, 10)),
        ("Eylül'ün 3. haftası", date(2026, 9, 15), date(2026, 9, 21)),
        ("eylul 1. hafta", date(2026, 9, 1), date(2026, 9, 7)),
        # K2-B: beginning 1-10, middle 11-20, end 21 to the last day; a month after this one is last year's.
        ("Eylül başında ne harcadım?", date(2026, 9, 1), date(2026, 9, 10)),
        ("Kasım ortasında ne harcadım?", date(2025, 11, 11), date(2025, 11, 20)),
        ("Eylül sonu ne harcadım?", date(2026, 9, 21), date(2026, 9, 30)),
        ("Ağustos sonunda", date(2026, 8, 21), date(2026, 8, 31)),
        # Since the new year, last year, N days ago, the day before yesterday.
        ("Yılbaşından beri ne harcadım?", date(2026, 1, 1), date(2026, 10, 10)),
        ("yılbaşından bu yana", date(2026, 1, 1), date(2026, 10, 10)),
        ("Geçen yıl ne harcadım?", date(2025, 1, 1), date(2025, 12, 31)),
        ("geçen sene", date(2025, 1, 1), date(2025, 12, 31)),
        ("3 gün önce ne harcadım?", date(2026, 10, 7), date(2026, 10, 7)),
        ("Evvelsi gün ne harcadım?", date(2026, 10, 8), date(2026, 10, 8)),
        # "Geçen <gün>" is the latest such day before today, never today itself (today is a Saturday).
        ("Geçen salı ne harcadım?", date(2026, 10, 6), date(2026, 10, 6)),
        ("geçen pazartesi", date(2026, 10, 5), date(2026, 10, 5)),
        ("geçen cuma günü", date(2026, 10, 9), date(2026, 10, 9)),
        ("geçen cumartesi", date(2026, 10, 3), date(2026, 10, 3)),
        ("geçen pazar", date(2026, 10, 4), date(2026, 10, 4)),
        ("Geçen hafta sonu ne harcadım?", date(2026, 10, 3), date(2026, 10, 4)),
        # K4-A: N weeks ago is that Monday-Sunday week, N months ago that whole month, N years ago that whole year.
        ("2 hafta önce ne harcadım?", date(2026, 9, 21), date(2026, 9, 27)),
        ("1 hafta önce", date(2026, 9, 28), date(2026, 10, 4)),
        ("3 ay önce ne harcadım?", date(2026, 7, 1), date(2026, 7, 31)),
        ("1 ay önce", date(2026, 9, 1), date(2026, 9, 30)),
        ("1 yıl önce", date(2025, 1, 1), date(2025, 12, 31)),
    ],
)
def test_an_expression_with_a_fixed_rule_is_read_by_code(message, start, end):
    assert found(message) == (start, end)


@pytest.mark.parametrize(
    ("message", "start", "end"),
    [
        # The live trial's day, a Friday (27 November 2026).
        ("Yılbaşından beri ne kadar harcadım?", date(2026, 1, 1), date(2026, 11, 27)),
        ("Eylül'ün ilk haftası ne harcadım?", date(2026, 9, 1), date(2026, 9, 7)),
        ("Geçen salı ne harcadım?", date(2026, 11, 24), date(2026, 11, 24)),
        ("Geçen cuma ne harcadım?", date(2026, 11, 20), date(2026, 11, 20)),
        ("Geçen hafta sonu", date(2026, 11, 21), date(2026, 11, 22)),
        ("2 hafta önce", date(2026, 11, 9), date(2026, 11, 15)),
        ("3 ay önce", date(2026, 8, 1), date(2026, 8, 31)),
        ("3 gün önce ne harcadım?", date(2026, 11, 24), date(2026, 11, 24)),
        ("Kasım sonu", date(2026, 11, 21), date(2026, 11, 27)),
    ],
)
def test_the_rules_hold_on_another_day(message, start, end):
    reading = read_dates(message, date(2026, 11, 27))

    assert (reading.kind, reading.start, reading.end) == ("found", start, end)


def test_a_part_of_a_month_that_has_not_begun_is_not_yet():
    reading = read_dates("Ekim sonu ne harcadım?", TODAY)

    assert reading.kind == "invalid" and "henüz gelmedi" in reading.reason


@pytest.mark.parametrize(
    ("message", "words"),
    [
        ("Yılbaşından beri ne harcadım?", "Yılbaşından beri"),
        ("Eylül'ün ilk haftası ne harcadım?", "Eylül'ün ilk haftası"),
        ("Geçen salı ne harcadım?", "Geçen salı"),
        ("geçen cuma günü market", "geçen cuma günü"),
        ("Migros'a 2 hafta önce ne verdim?", "2 hafta önce"),
        ("Salı günü ne harcadım?", "Salı günü"),
    ],
)
def test_the_span_holds_the_words_of_the_date(message, words):
    reading = read_dates(message, TODAY)

    assert message[slice(*reading.span)] == words


# --- K1-C: a range the model gave must fit the words it was read from -----------------------------------------------------------

from dates import fits_words  # noqa: E402


@pytest.mark.parametrize(
    ("expression", "start", "end", "fits"),
    [
        # The case found live: "the second week of October" came back as a week of September.
        ("ekimin ikinci haftası", date(2026, 9, 14), date(2026, 9, 20), False),
        ("ekimin ikinci haftası", date(2026, 10, 8), date(2026, 10, 14), True),
        ("Salı günü", date(2026, 10, 5), date(2026, 10, 5), False),
        ("Salı günü", date(2026, 10, 6), date(2026, 10, 6), True),
        ("cumartesi", date(2026, 10, 9), date(2026, 10, 9), False),
        ("cuma", date(2026, 10, 9), date(2026, 10, 9), True),
        ("2025 yılbaşında", date(2026, 1, 1), date(2026, 1, 1), False),
        ("2025 yılbaşında", date(2025, 1, 1), date(2025, 1, 1), True),
        # Nothing in the words to hold the range to: the code checks of check_range are all there is.
        ("bayramda", date(2026, 3, 20), date(2026, 3, 22), True),
        ("03/09", date(2026, 9, 3), date(2026, 9, 3), True),
    ],
)
def test_a_range_the_model_gave_must_fit_the_month_weekday_and_year_written(expression, start, end, fits):
    assert fits_words(expression, start, end) is fits


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
