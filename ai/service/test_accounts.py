"""ACC-01b: the word rules of the everyday banking questions, read by code (K2-C, K3-A, K6-C)."""

from decimal import Decimal

import pytest

from accounts import (BALANCE, INCOME, SEARCH, TRANSFERS, Bounds, amount_bounds, code_arguments, fallback_arguments,
                      implied_this_month, tool_of, transfer_direction)
from answer_check import fold_turkish


def tool(message):
    return tool_of(fold_turkish(message))


# --- Which tool the words name (K2-C) ------------------------------------------------------------------------------------------

@pytest.mark.parametrize("message", ["Bakiyem ne kadar?", "Hesabımda ne kadar para var?", "hesabimda ne kadar var",
                                     "Hesaplarımı göster", "Ne kadar param var?", "Param kaldı mı?", "BAKİYEM"])
def test_balance_words_name_the_accounts_tool(message):
    assert tool(message) == BALANCE


@pytest.mark.parametrize("message", ["Maaşım yattı mı?", "Geçen ay maaşım ne kadar yattı?", "Bu ay ne kadar gelirim oldu?",
                                     "maasim yatti mi", "Bursum yattı mı?", "Geçen ay gelirlerim neler?"])
def test_income_words_name_the_income_tool(message):
    assert tool(message) == INCOME


@pytest.mark.parametrize("message", ["Geçen ay kime para gönderdim?", "Geçen ay kimden para geldi?", "Bu ayki transferlerim",
                                     "Selin'e ne kadar gönderdim?", "Geçen ay havale ettiklerim", "gecen ay kimden para geldi",
                                     "Hasan'dan para geldi mi?"])
def test_transfer_words_name_the_transfer_tool(message):
    assert tool(message) == TRANSFERS


@pytest.mark.parametrize("message", ["Geçen ay 2.000 TL üstü harcamalarım", "Bu ay 500 liranın altındaki harcamalarım",
                                     "Geçen ay 1.000 ile 5.000 TL arası harcamalar", "Son 3 ayda 5 bin TL'den fazla harcamalarım"])
def test_a_spending_question_with_an_amount_limit_names_the_search(message):
    assert tool(message) == SEARCH


@pytest.mark.parametrize("message", [
    # The income share and the advice keep their own tools (INC-03): spending, a share or a what-if beside the salary.
    "Geçen ay maaşımın ne kadarını harcadım?", "Gelirime göre harcamam nasıl?", "Maaşımın yüzde kaçı gitti?",
    "Maaşımı dolar olarak biriktirseydim?",
    # A spending question without a limit, or a limit without spending, is not a search.
    "Geçen ay en büyük harcamalarım neler?", "Geçen ay en fazla hangi kategoriye harcadım?", "2.000 TL üstü",
    # Spending from an account is not its balance.
    "Hesabımdan bu ay ne harcadım?", "Hesabımdaki harcamalar",
    # Planning keeps its own words.
    "Ayda 2.000 TL'den fazla harcamamak için bütçem 5.000 TL",
])
def test_other_questions_name_no_account_tool(message):
    assert tool(message) is None


# --- The amount limit (K3-A) ---------------------------------------------------------------------------------------------------

@pytest.mark.parametrize("message, bounds", [
    ("Geçen ay 2.000 TL üstü harcamalarım", Bounds(Decimal(2000), None, "TRY")),
    ("Geçen ay 2.000 TL'den fazla harcamalarım", Bounds(Decimal(2000), None, "TRY")),
    ("2000 tl uzeri harcamalarim", Bounds(Decimal(2000), None, "TRY")),
    ("5 bin TL'yi aşan harcamalarım", Bounds(Decimal(5000), None, "TRY")),
    ("En az 750 TL olan harcamalarım", Bounds(Decimal(750), None, "TRY")),
    ("Bu ay 500 liranın altındaki harcamalarım", Bounds(None, Decimal(500), "TRY")),
    ("100 TL'den az harcamalarım", Bounds(None, Decimal(100), "TRY")),
    ("En fazla 250 TL olan harcamalarım", Bounds(None, Decimal(250), "TRY")),
    ("1.000 ile 5.000 TL arası harcamalar", Bounds(Decimal(1000), Decimal(5000), "TRY")),
    ("5.000 ile 1.000 TL arasındaki harcamalar", Bounds(Decimal(1000), Decimal(5000), "TRY")),
    # A bare amount is lira; a named currency stays the user's.
    ("200 üstü harcamalarım", Bounds(Decimal(200), None, "TRY")),
    ("50 dolar üstü harcamalarım", Bounds(Decimal(50), None, "USD")),
    ("100 euro altı harcamalarım", Bounds(None, Decimal(100), "EUR")),
])
def test_the_limit_and_its_side_are_read_from_the_words(message, bounds):
    assert amount_bounds(message) == bounds


@pytest.mark.parametrize("message", ["Geçen ay 2.000 TL harcadım mı?", "Son 3 ayda harcamalarım", "En fazla hangi işyerine harcadım?",
                                     "Son 3 ay üstü harcamalarım", "2.000 ve 3.000 TL üstü harcamalarım"])
def test_no_limit_or_an_unclear_one_reads_as_none(message):
    # "Son 3 ay" is a period, not 3 lira; two limits on one side cannot both be the user's.
    assert amount_bounds(message) is None


# --- The direction of a transfer (code, like the limit) -------------------------------------------------------------------------

@pytest.mark.parametrize("message, direction", [
    ("Geçen ay kime para gönderdim?", "Sent"), ("Selin'e ne kadar yolladım?", "Sent"), ("Gönderdiğim paralar", "Sent"),
    ("Havale ettiklerim", "Sent"), ("Giden transferlerim", "Sent"),
    ("Geçen ay kimden para geldi?", "Received"), ("Gelen transferler", "Received"), ("Hasan bana para gönderdi mi?", "Received"),
    ("Bu ayki transferlerim", None), ("Gelen ve giden transferlerim", None),
])
def test_the_direction_is_read_from_the_words(message, direction):
    assert transfer_direction(message) == direction


# --- "Yattı mı?" means this month (K6-C) ---------------------------------------------------------------------------------------

@pytest.mark.parametrize("tool_name, message, implied", [
    (INCOME, "Maaşım yattı mı?", True), (INCOME, "maasim yatti mi", True), (TRANSFERS, "Hasan'dan para geldi mi?", True),
    (INCOME, "Geçen ay maaşım yattı mı?", False),   # a period said is the user's
    (TRANSFERS, "Kime para gönderdim?", False),    # asked, not guessed
    (INCOME, "Gelirim ne kadar?", False),
    (SEARCH, "2.000 TL üstü harcama geldi mi?", False),
])
def test_only_did_it_arrive_without_a_period_means_this_month(tool_name, message, implied):
    assert implied_this_month(tool_name, message) is implied


# --- Code's arguments over the model's -----------------------------------------------------------------------------------------

def test_the_search_limit_is_code_s_whatever_the_model_wrote():
    arguments = code_arguments(SEARCH, {"period": "LastMonth", "maxAmount": 2000, "currencyCode": "USD"},
                               "Geçen ay 2.000 TL üstü harcamalarım", None)
    assert arguments == {"period": "LastMonth", "minAmount": 2000, "currencyCode": "TRY"}


def test_a_limit_the_words_do_not_give_is_dropped_unless_a_follow_up_keeps_it():
    model = {"period": "ThisMonth", "minAmount": 2000, "currencyCode": "TRY"}
    assert code_arguments(SEARCH, model, "Bu ay ne harcadım?", None) == {"period": "ThisMonth"}
    # "Peki bu ay?" after the 2.000 TL search keeps its limit (CHAT-10 D12-A: a figure carries only after "peki/ya").
    previous = {"period": "LastMonth", "minAmount": 2000, "currencyCode": "TRY"}
    assert code_arguments(SEARCH, {"period": "ThisMonth"}, "Peki bu ay?", previous) == model


def test_the_transfer_direction_is_code_s_and_kept_by_a_follow_up():
    assert code_arguments(TRANSFERS, {"period": "LastMonth", "direction": "Received"}, "Geçen ay kime para gönderdim?", None) == {
        "period": "LastMonth", "direction": "Sent"}
    assert code_arguments(TRANSFERS, {"period": "LastMonth", "direction": "Sent"}, "Bu ayki transferlerim", None) == {
        "period": "LastMonth"}
    assert code_arguments(TRANSFERS, {"period": "ThisMonth"}, "Peki bu ay?", {"period": "LastMonth", "direction": "Sent"}) == {
        "period": "ThisMonth", "direction": "Sent"}


def test_other_tools_keep_their_arguments():
    assert code_arguments(INCOME, {"period": "LastMonth"}, "Geçen ay gelirim?", None) == {"period": "LastMonth"}


# --- When the model chose nothing (K2-C, the K13-A pattern) ---------------------------------------------------------------------

def test_the_fallback_reads_the_period_and_the_figures_from_the_words():
    assert fallback_arguments(INCOME, "Geçen ay maaşım ne kadar yattı?") == {"period": "LastMonth"}
    assert fallback_arguments(TRANSFERS, "Geçen ay kimden para geldi?") == {"period": "LastMonth", "direction": "Received"}
    assert fallback_arguments(SEARCH, "Geçen ay 2.000 TL üstü harcamalarım") == {"period": "LastMonth", "minAmount": 2000,
                                                                                  "currencyCode": "TRY"}
    assert fallback_arguments(BALANCE, "Bakiyem ne kadar?") == {}


def test_the_fallback_without_a_period_asks_it():
    assert fallback_arguments(TRANSFERS, "Kime para gönderdim?") is None


def test_a_counterparty_the_user_did_not_name_is_dropped_or_carried():
    # K4-B: the model writes the name, code checks it is the user's word; the backend then matches it against the customer's own.
    named = code_arguments(TRANSFERS, {"period": "LastMonth", "counterparty": "Selin"}, "Geçen ay Selin'e ne gönderdim?", None)
    assert named == {"period": "LastMonth", "counterparty": "Selin", "direction": "Sent"}
    guessed = code_arguments(TRANSFERS, {"period": "LastMonth", "counterparty": "Annem"}, "Geçen ay kime para gönderdim?", None)
    assert guessed == {"period": "LastMonth", "direction": "Sent"}
    carried = code_arguments(TRANSFERS, {"period": "ThisMonth", "counterparty": "Selin"}, "Peki bu ay?",
                             {"period": "LastMonth", "counterparty": "Selin"})
    assert carried == {"period": "ThisMonth", "counterparty": "Selin"}


def test_an_argument_the_model_left_null_is_left_out():
    # ACC-01b PR2: the 4b model wrote "merchant": null (A13 of the 16 Nov run); the reply carries arguments as text, so the .NET link
    # would have searched for a merchant called "null" and shown an empty list next to the right figure.
    assert code_arguments(SEARCH, {"period": "ThisMonth", "merchant": None, "categoryCode": None}, "Bu ay 500 liranın altındaki harcamalarım",
                          None) == {"period": "ThisMonth", "maxAmount": 500, "currencyCode": "TRY"}
    assert code_arguments(INCOME, {"period": "ThisMonth", "x": None}, "Bu ay gelirim", None) == {"period": "ThisMonth"}
