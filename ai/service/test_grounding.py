import pytest

from grounding import check_arguments, count_in, mentions_count, mentions_period, period_in

# CHAT-03 K2-A: an argument the model filled in must come from the user's own words. Expected values are written by hand.

PERIOD = {"type": "string", "enum": ["Today", "Yesterday", "ThisWeek", "LastWeek", "Last7Days", "Last30Days",
                                     "ThisMonth", "LastMonth", "Last3Months", "Last6Months", "ThisYear"]}
SUMMARY = {"type": "object", "properties": {"period": PERIOD}, "required": ["period"]}
MERCHANT = {"type": "object", "properties": {"merchant": {"type": "string"}, "period": PERIOD}, "required": ["merchant", "period"]}
TREND = {"type": "object", "properties": {"months": {"type": "integer", "minimum": 1, "maximum": 12}}, "required": ["months"]}
UPCOMING = {"type": "object", "properties": {"days": {"type": "integer", "minimum": 1, "maximum": 31, "default": 30}}}


@pytest.mark.parametrize("message", [
    "Dün ne harcadım?", "Bugün ne kadar harcadım?", "Bu hafta ne harcadım?", "Son 30 günde kategorilere göre harcamam nasıl?",
    "Bu yıl toplam ne kadar harcadım?", "Geçen ay en çok hangi kategoriye harcadım?", "Eylül'de ne harcadım?",
    "Son 3 ayda Starbucks'a ne kadar harcadım?", "GEÇEN AYIN harcaması", "Bu sene ne harcadım?", "Dünkü harcamam",
    "Geçen hafta Starbucks'a ne kadar verdim?", "Ekimde ne kadar harcadım?",
])
def test_a_period_in_the_users_words_is_found(message):
    assert mentions_period(message)


@pytest.mark.parametrize("message", [
    "Ne kadar harcadım?", "Starbucks'a ne kadar harcadım?", "Kategorilere göre harcamam nasıl?", "Aylık harcamam ne kadar?",
    "Harcamam aylara göre nasıl değişti?", "Marketteki harcamam ne kadar?", "En çok nereye harcadım?",
])
def test_no_period_in_the_users_words(message):
    assert not mentions_period(message)


@pytest.mark.parametrize(("reply", "period"), [
    ("geçen ay", "LastMonth"), ("Bu ay", "ThisMonth"), ("son 3 ay", "Last3Months"), ("son üç ay", "Last3Months"),
    ("Son 6 ayı göster", "Last6Months"), ("dün", "Yesterday"), ("bugün", "Today"), ("geçen hafta", "LastWeek"),
    ("bu hafta", "ThisWeek"), ("bu sene", "ThisYear"), ("bu yıl", "ThisYear"), ("son 30 gün", "Last30Days"),
    ("son 7 gün", "Last7Days"), ("GEÇEN AYIN", "LastMonth"),
])
def test_a_reply_names_one_of_the_tools_periods(reply, period):
    assert period_in(reply) == period


@pytest.mark.parametrize("reply", ["Eylül", "Starbucks", "geçen yıl", "son 5 ay", ""])
def test_a_reply_without_one_of_the_tools_periods(reply):
    # "geçen yıl" and "son 5 ay" are periods, but not ones the tools have: nothing is rounded to the nearest one.
    assert period_in(reply) is None


@pytest.mark.parametrize(("message", "value", "expected"), [
    ("Son 4 ayın aylık harcamaları", 4, True),
    ("Son altı ayda aylara göre nasıl değişti?", 6, True),
    ("Son bir yılın aylık harcaması", 12, True),
    ("Önümüzdeki 7 günde hangi ödemelerim var?", 7, True),
    ("Harcamam aylara göre nasıl değişti?", 6, False),
    ("Yaklaşan ödemelerim neler?", 30, False),
    ("Son 3 ayda harcamam", 6, False),
])
def test_a_count_must_be_written_by_the_user(message, value, expected):
    assert mentions_count(message, value) is expected


@pytest.mark.parametrize(("reply", "count"), [("6", 6), ("son 12 ay", 12), ("altı", 6), ("üç aylık olsun", 3), ("on iki", 12),
                                             ("bilmiyorum", None), ("", None)])
def test_a_count_is_read_from_a_reply(reply, count):
    assert count_in(reply) == count


def test_a_guessed_period_is_reported_as_missing():
    result = check_arguments({"period": "ThisMonth"}, SUMMARY, "Ne kadar harcadım?")
    assert (result.missing, result.arguments) == ("period", {"period": "ThisMonth"})


def test_a_guessed_count_is_reported_as_missing():
    assert check_arguments({"months": 6}, TREND, "Harcamam aylara göre nasıl değişti?").missing == "months"


def test_a_guessed_optional_count_is_dropped_so_the_backend_default_applies():
    # "Yaklaşan ödemelerim neler?": the model wrote days=30; nothing is asked, the backend's own 30 is used.
    result = check_arguments({"days": 30}, UPCOMING, "Yaklaşan ödemelerim neler?")
    assert (result.missing, result.arguments) == (None, {})


def test_grounded_arguments_pass_unchanged():
    arguments = {"merchant": "Starbucks", "period": "LastMonth"}
    assert check_arguments(arguments, MERCHANT, "Geçen ay Starbucks'a ne kadar harcadım?").missing is None
    assert check_arguments(arguments, MERCHANT, "Geçen ay Starbucks'a ne kadar harcadım?").arguments == arguments
    assert check_arguments({"days": 7}, UPCOMING, "Önümüzdeki 7 günde ne ödeyeceğim?").arguments == {"days": 7}


def test_the_period_is_asked_before_anything_else():
    # Both the merchant and the period are there, but only the period is the model's invention.
    assert check_arguments({"merchant": "Starbucks", "period": "ThisYear"}, MERCHANT, "Starbucks'a ne kadar harcadım?").missing == "period"


# CHAT-02 K1-C: in a follow-up, an argument may also come unchanged from the previous call.

LAST_MONTH_STARBUCKS = {"merchant": "Starbucks", "period": "LastMonth"}


def test_a_follow_up_keeps_the_previous_period():
    result = check_arguments({"merchant": "Kahve Durağı", "period": "LastMonth"}, MERCHANT, "Peki Kahve Durağı'na?",
                             previous=LAST_MONTH_STARBUCKS)
    assert (result.missing, result.arguments) == (None, {"merchant": "Kahve Durağı", "period": "LastMonth"})


def test_a_period_neither_said_nor_carried_over_is_still_a_guess():
    result = check_arguments({"merchant": "Kahve Durağı", "period": "ThisYear"}, MERCHANT, "Peki Kahve Durağı'na?",
                             previous=LAST_MONTH_STARBUCKS)
    assert result.missing == "period"


def test_a_count_may_come_from_the_message_or_the_previous_call():
    assert check_arguments({"months": 6}, TREND, "6 aylık göster", previous={"months": 3}).missing is None
    assert check_arguments({"months": 3}, TREND, "Peki kategoriye göre?", previous={"months": 3}).missing is None
    assert check_arguments({"months": 12}, TREND, "Peki kategoriye göre?", previous={"months": 3}).missing == "months"


def test_a_period_carried_over_across_tools_counts_too():
    # "Bu ay en çok hangi kategoriye harcadım?" → "Peki işyerlerine göre?": another tool, the same period.
    assert check_arguments({"period": "ThisMonth"}, SUMMARY, "Peki işyerlerine göre?", previous={"period": "ThisMonth"}).missing is None


def test_a_merchant_not_said_in_a_follow_up_stays_the_previous_one():
    # The live trial: "Starbucks" → "Peki Kahve Durağı'na?" → "Ya son 3 ayda?" came back with Starbucks (930,00 TL instead of
    # Kahve Durağı's 325,00 TL). A merchant the user did not name in a follow-up is the one of the last call.
    result = check_arguments({"merchant": "Starbucks", "period": "Last3Months"}, MERCHANT, "Ya son 3 ayda?",
                             previous={"merchant": "Kahve Durağı", "period": "LastMonth"})
    assert (result.missing, result.arguments) == (None, {"merchant": "Kahve Durağı", "period": "Last3Months"})


def test_a_merchant_named_in_the_follow_up_is_kept():
    result = check_arguments({"merchant": "Kahve Durağı", "period": "LastMonth"}, MERCHANT, "Peki KAHVE DURAĞI'na?",
                             previous=LAST_MONTH_STARBUCKS)
    assert result.arguments["merchant"] == "Kahve Durağı"


def test_without_a_previous_call_the_merchant_is_left_to_the_backend():
    # Outside a follow-up the backend's MerchantMatcher decides (CHAT-03), as before.
    result = check_arguments({"merchant": "Kahve", "period": "LastMonth"}, MERCHANT, "Geçen ay kahveye ne kadar verdim?")
    assert result.arguments == {"merchant": "Kahve", "period": "LastMonth"}
