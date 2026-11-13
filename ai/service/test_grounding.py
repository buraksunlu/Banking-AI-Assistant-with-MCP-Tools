from decimal import Decimal

import pytest

from grounding import (Grounding, months_in, unclear_amounts, mentions_category, amounts_in, check_arguments, count_in, currency_in, mentions_amount, mentions_count, mentions_currency,
                       mentions_percent, mentions_period, percent_in, period_in)

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
    ("Geçen yılın aylık harcaması", 12, True),
    # MCP-18: "yılda" is the annual saving the user asks about, not twelve months of history (the eval's S43).
    ("Yeme içme harcamamı %20 azaltırsam yılda ne kadar biriktiririm?", 12, False),
    ("Bu yıl aylara göre nasıl değişti?", 12, False),  # this year so far is ten months, not twelve
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


# MCP-17/18 K2-A, K3-A, K4-A: the budget, its currency and the cut are the user's own figures, never the model's.

CATEGORY = {"type": ["string", "null"], "enum": ["FOOD", "GROCERY", "OTHER", None], "default": None}
OPTIONAL_TEXT = {"type": ["string", "null"], "default": None}
BUDGET = {"type": "object", "properties": {
    "amount": {"type": "number"}, "currencyCode": {"type": "string", "enum": ["TRY", "USD", "EUR", "GBP"]},
    "categoryCode": CATEGORY, "merchant": OPTIONAL_TEXT}, "required": ["amount", "currencyCode"]}
SAVINGS = {"type": "object", "properties": {
    "reductionPercent": {"type": "integer", "minimum": 1, "maximum": 100}, "categoryCode": CATEGORY, "merchant": OPTIONAL_TEXT,
    "months": {"type": "integer", "default": 6, "minimum": 1, "maximum": 12}}, "required": ["reductionPercent"]}


@pytest.mark.parametrize(("text", "amounts"), [
    ("Bu ay 1.000 TL bütçem var", ["1000"]), ("3000 liralık bütçe", ["3000"]), ("2.500,50 TL", ["2500.50"]),
    ("5 bin lira ayırdım", ["5000"]), ("bin liram var", ["1000"]), ("1,5 bin TL", ["1500"]), ("10.000TL", ["10000"]),
    ("Bütçemi aşar mıyım?", []),
])
def test_amounts_are_read_the_turkish_way(text, amounts):
    assert amounts_in(text) == [Decimal(a) for a in amounts]


# CHAT-10 K9-C: a unit right after the number is read ("5k" was 5 lira in the INC-04 trial); a letter the code does not know makes
# the number no amount at all, so it is asked back instead of read as something else.
@pytest.mark.parametrize(("text", "amounts"), [
    ("5k", ["5000"]), ("5K", ["5000"]), ("5 k", ["5000"]), ("2,5k", ["2500"]), ("10k TL", ["10000"]), ("bütçem 5k'lık", ["5000"]),
    ("5bin", ["5000"]), ("5 binlik bütçe", ["5000"]), ("5 milyon", ["5000000"]), ("1,5 milyon lira", ["1500000"]),
    ("3000TL", ["3000"]), ("5000lik bütçe", ["5000"]), ("5 kişiyiz", ["5"]),
    ("5x", []), ("5abc TL", []), ("kabin", []),
])
def test_a_unit_after_the_number_is_read_and_an_unknown_one_is_no_amount(text, amounts):
    assert amounts_in(text) == [Decimal(a) for a in amounts]


@pytest.mark.parametrize(("text", "unclear"), [
    ("5x", ["5x"]), ("bütçem 5abc", ["5abc"]), ("5k", []), ("3000TL", []), ("5bin", []), ("5000lik", []), ("Bu ay 1.000 TL", []),
])
def test_the_amounts_the_code_cannot_read(text, unclear):
    assert unclear_amounts(text) == unclear


def test_a_budget_in_thousands_is_the_users_own_figure():
    assert mentions_amount("Bu ay 5k bütçem var", 5000)
    assert not mentions_amount("Bu ay 5k bütçem var", 5)


@pytest.mark.parametrize(("message", "value", "expected"), [
    ("Bu ay 1.000 TL bütçem var", 1000, True),
    ("Bu ay 1.000 TL bütçem var", 1000.0, True),
    ("Bu ay 1.000 TL bütçem var", 1.0, False),          # "1.000" read as one: the error this check exists for
    ("Market için 2.500,50 TL ayırdım", 2500.5, True),
    ("5 bin liralık bütçem var", 5000, True),
    ("Bütçemi aşacak mıyım?", 5000, False),
    ("Bu ay 1.000 TL bütçem var", True, False),
])
def test_an_amount_must_be_written_by_the_user(message, value, expected):
    assert mentions_amount(message, value) is expected


@pytest.mark.parametrize(("text", "currency"), [
    ("1.000 TL", "TRY"), ("3000TL", "TRY"), ("500 lira", "TRY"), ("500 liralık", "TRY"), ("₺500", "TRY"), ("1.500 euro", "EUR"),
    ("€200", "EUR"), ("200 avro", "EUR"), ("100 dolar", "USD"), ("$100", "USD"), ("50 sterlin", "GBP"), ("£50", "GBP"),
    ("500 bütçem var", None), ("Atletizm kursu", None), ("1.000 TL ya da 50 euro", None),  # two currencies: not one choice
])
def test_the_currency_a_text_names(text, currency):
    assert currency_in(text) == currency


def test_a_currency_must_be_named_by_the_user():
    assert mentions_currency("Bu ay 1.000 TL bütçem var", "TRY")
    assert not mentions_currency("Bu ay 1.000 bütçem var", "TRY")
    assert not mentions_currency("Bu ay 1.000 euro bütçem var", "TRY")


@pytest.mark.parametrize(("text", "value"), [
    ("%20 azaltsam", 20), ("20% azaltırsam", 20), ("yüzde 20", 20), ("yüzde yirmi", 20), ("yüzde yirmi beş", 25),
    ("Starbucks'ı yarıya indirsem", 50), ("yarı yarıya azaltsam", 50), ("çeyreğini kessem", 25), ("tamamen bıraksam", 100),
    ("Son 3 aya göre %20 azaltırsam", 20), ("biraz azaltsam", None), ("ne kazanırım?", None), ("Yarın azaltsam", None),
])
def test_the_cut_a_text_names(text, value):
    assert percent_in(text) == value


def test_a_cut_must_be_named_by_the_user():
    assert mentions_percent("Starbucks harcamamı yarıya indirsem?", 50)
    assert not mentions_percent("Starbucks harcamamı biraz azaltsam?", 20)
    # "3" is a count of months here, not a cut.
    assert not mentions_percent("Son 3 aya göre azaltsam?", 3)


def test_a_budget_in_the_users_words_passes():
    arguments = {"amount": 1000, "currencyCode": "TRY", "categoryCode": "FOOD"}
    result = check_arguments(arguments, BUDGET, "Bu ay yeme içme için 1.000 TL bütçem var, aşar mıyım?")
    assert (result.missing, result.unsaid, result.arguments) == (None, (), arguments)


def test_a_misread_amount_is_asked_back():
    result = check_arguments({"amount": 1.0, "currencyCode": "TRY"}, BUDGET, "Bu ay 1.000 TL bütçem var, ne durumdayım?")
    assert result.missing == "amount"


def test_a_guessed_currency_is_asked_back():
    result = check_arguments({"amount": 500, "currencyCode": "TRY"}, BUDGET, "Bu ay 500 bütçem var, ne durumdayım?")
    assert (result.missing, result.unsaid) == ("currencyCode", ("currencyCode",))


def test_the_amount_is_asked_before_the_currency_and_both_are_reported():
    # The model invented both; they are asked in the tool's own order, the reply to the first may carry the second.
    result = check_arguments({"currencyCode": "TRY", "amount": 5000}, BUDGET, "Bu ay bütçemi aşacak mıyım?")
    assert (result.missing, result.unsaid) == ("amount", ("amount", "currencyCode"))


def test_a_required_figure_the_model_left_out_is_asked_too():
    # Without this the server would refuse the call and the user would only see the general question.
    assert check_arguments({"currencyCode": "TRY"}, BUDGET, "Bu ay TL bütçemi aşar mıyım?").missing == "amount"
    assert check_arguments({"merchant": "Starbucks"}, SAVINGS, "Starbucks'tan ne kadar tasarruf ederim?").missing == "reductionPercent"


def test_a_cut_in_the_users_words_passes_and_guessed_months_are_dropped():
    result = check_arguments({"reductionPercent": 50, "merchant": "Starbucks", "months": 12}, SAVINGS,
                             "Starbucks harcamamı yarıya indirsem ne kazanırım?")
    assert (result.missing, result.arguments) == (None, {"reductionPercent": 50, "merchant": "Starbucks"})


def test_months_the_user_gave_are_kept():
    result = check_arguments({"reductionPercent": 20, "categoryCode": "GROCERY", "months": 3}, SAVINGS,
                             "Son 3 aya göre market harcamamı %20 azaltırsam ne kadar biriktiririm?")
    assert result.arguments == {"reductionPercent": 20, "categoryCode": "GROCERY", "months": 3}


def test_a_guessed_cut_is_asked_back():
    assert check_arguments({"reductionPercent": 20}, SAVINGS, "Harcamamı biraz azaltsam ne olur?").missing == "reductionPercent"


def test_a_follow_up_keeps_the_previous_budgets_currency_and_target():
    previous = {"amount": 1000, "currencyCode": "TRY", "categoryCode": "FOOD"}
    result = check_arguments({"amount": 1500, "currencyCode": "TRY", "categoryCode": "FOOD"}, BUDGET, "Peki 1.500 olsa?", previous)
    assert (result.missing, result.arguments) == (None, {"amount": 1500, "currencyCode": "TRY", "categoryCode": "FOOD"})
    assert check_arguments({"amount": 2000, "currencyCode": "TRY"}, BUDGET, "Peki 1.500 olsa?", previous).missing == "amount"


def test_a_blank_optional_argument_is_left_out():
    # The eval's S39: the model wrote categoryCode "" for a total budget; the server refuses "" (not one of the codes), so the
    # user would have got the general question instead of the answer.
    result = check_arguments({"amount": 1500, "currencyCode": "EUR", "categoryCode": "", "merchant": "  "}, BUDGET,
                             "Bu ay toplam 1.500 euro bütçem var, ne durumdayım?")
    assert (result.missing, result.arguments) == (None, {"amount": 1500, "currencyCode": "EUR"})


def test_a_blank_required_argument_is_left_to_the_server():
    # Not a figure the chat can ask for: the server refuses it and the user gets the general question, as before.
    result = check_arguments({"merchant": "", "period": "LastMonth"}, MERCHANT, "Geçen ay ne kadar harcadım?")
    assert (result.missing, result.arguments) == (None, {"merchant": "", "period": "LastMonth"})


# --- INC-03 PR2: a category the user did not name is the model's guess ----------------------------------------------------------

@pytest.mark.parametrize(("message", "code", "said"), [
    ("Geçen ay eğlence harcamamı altın alsaydım", "ENTERTAINMENT", True),
    ("Yeme içme için bütçem 1.000 TL", "FOOD", True),
    ("Market harcamamı %20 azaltsam", "GROCERY", True),
    ("Ulaşım giderlerim ne kadar", "TRANSPORTATION", True),
    ("Geçen ay harcadığım parayla altın alsaydım", "OTHER", False),      # measured: the model wrote OTHER here
    ("Geçen hafta harcamasaydım da euro alsaydım", "FOOD", False),
    ("Marketing harcamam", "GROCERY", True),                              # a word beginning is enough ("market…"), as for merchants
    ("Eğlence harcamam", "FOOD", False),
    ("Eğlence harcamam", "NOT_A_CODE", False),
])
def test_a_category_counts_only_when_the_user_said_it(message, code, said):
    assert mentions_category(message, code) is said


def test_an_invented_optional_category_is_dropped_and_a_said_one_is_kept():
    schema = {"properties": {"period": {"type": "string"}, "categoryCode": {"type": ["string", "null"]}}, "required": ["period"]}

    invented = check_arguments({"period": "LastMonth", "categoryCode": "OTHER"}, schema, "Geçen ay harcadığım parayla altın alsaydım")
    said = check_arguments({"period": "LastMonth", "categoryCode": "ENTERTAINMENT"}, schema, "Geçen ay eğlence harcamamı altın alsaydım")
    carried = check_arguments({"period": "LastMonth", "categoryCode": "FOOD"}, schema, "Peki geçen ay?", {"categoryCode": "FOOD"})

    assert invented.arguments == {"period": "LastMonth"} and invented.missing is None
    assert said.arguments["categoryCode"] == "ENTERTAINMENT" and carried.arguments["categoryCode"] == "FOOD"


# --- CHAT-09 PR2 K12-A: the period the user named wins over the model's --------------------------------------------------------

@pytest.mark.parametrize(("message", "written", "run"), [
    ("Geçen hafta ne kadar harcadım?", "Last7Days", "LastWeek"),       # 28 Sep - 4 Oct asked, 4 - 10 Oct would have been answered
    ("GEÇEN HAFTA NE KADAR HARCADIM?", "ThisWeek", "LastWeek"),
    ("Dun ne kadar harcadim?", "Today", "Yesterday"),
    ("Son 30 günde ne harcadım?", "ThisMonth", "Last30Days"),
    ("Bu ay ne harcadım?", "ThisMonth", "ThisMonth"),                   # the same period stays as it is
])
def test_the_period_the_user_named_is_run_whatever_the_model_wrote(message, written, run):
    assert check_arguments({"period": written}, SUMMARY, message) == Grounding(None, {"period": run})


def test_a_named_period_the_tool_does_not_offer_is_asked():
    # A complete-month tool offers LastMonth (and Custom) only: "bu hafta" cannot be run there and is not replaced by a guess.
    month = {"type": "object", "properties": {"period": {"type": "string", "enum": ["LastMonth", "Custom"]}}, "required": ["period"]}
    assert check_arguments({"period": "LastMonth"}, month, "Bu hafta maaşımın ne kadarını harcadım?").missing == "period"


def test_two_named_periods_keep_the_models_choice_only_when_it_is_one_of_them():
    message = "Bu hafta mı geçen hafta mı daha çok harcadım?"
    assert check_arguments({"period": "ThisWeek"}, SUMMARY, message).missing is None
    assert check_arguments({"period": "Last7Days"}, SUMMARY, message).missing == "period"


def test_a_follow_up_that_names_a_period_takes_it_although_the_last_call_had_another():
    # Before K12 the carried-over period passed first: "Peki geçen hafta?" ran last call's this month when the model kept it.
    previous = {"merchant": "Starbucks", "period": "ThisMonth"}
    assert check_arguments({"merchant": "Starbucks", "period": "ThisMonth"}, MERCHANT, "Peki geçen hafta?", previous) == \
        Grounding(None, {"merchant": "Starbucks", "period": "LastWeek"})


def test_a_period_said_only_by_a_month_name_is_left_to_the_date_reader():
    # "Eylül'de" names no tool period; the chat's date code puts the range in (CHAT-08), the model's period stays until then.
    assert check_arguments({"period": "ThisMonth"}, SUMMARY, "Eylül'de ne harcadım?") == Grounding(None, {"period": "ThisMonth"})


# INV-03 PR2 K5-A: a saving's duration, in months, as people say it. Hand-written values: a year is 12, "buçuk" half of the unit.
@pytest.mark.parametrize("text, months", [
    ("6 ay", [6]), ("altı ay", [6]), ("Altı Ay boyunca", [6]), ("6 aylık birikim", [6]), ("6ay", [6]), ("on iki ay", [12]),
    ("12 ayda", [12]), ("yirmi iki ay", [22]), ("1 yıl", [12]), ("bir yıl", [12]), ("bir yıllık", [12]), ("2 sene", [24]),
    ("1,5 yıl", [18]), ("bir buçuk yıl", [18]), ("yarım yıl", [6]), ("1 ay", [1]), ("6 ay ya da 1 yıl", [6, 12]),
    ("Ayda 1.000 TL 3 ay biriktirseydim", [3]),
])
def test_a_duration_is_read_in_months(text, months):
    assert months_in(text) == months


@pytest.mark.parametrize("text", [
    "Ayda 1.000 TL biriktirseydim", "her ay 500 TL", "aylık 2 bin", "bu yıl", "yıllık 12.000 TL", "3 ayakkabı aldım",
    "2,5 ay", "iki buçuk ay", "yarım ay", "her yıl",
])
def test_no_duration_or_a_part_of_a_month_is_none(text):
    assert months_in(text) == []


@pytest.mark.parametrize("text, amounts", [
    ("Ayda 1000 TL 6 ay biriktirseydim", [Decimal(1000)]), ("Ayda 5 bin 1 yıl", [Decimal(5000)]),
    ("6 aylık 2.000 TL", [Decimal(2000)]), ("1,5 yıl her ay 750 TL", [Decimal(750)]), ("6ay 300 TL", [Decimal(300)]),
])
def test_a_duration_is_no_amount(text, amounts):
    assert amounts_in(text) == amounts
    assert unclear_amounts(text) == []
