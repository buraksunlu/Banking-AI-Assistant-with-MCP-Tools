"""CHAT-12 PR3: the word rules behind the free phrasings that still got another tool's figure (free_eval.py; N19, N24, N36, N57,
N70 and the six balance and income phrasings only the model's intent question placed). Decisions K1-C (every wrong one gets its
rule), K2-A ("para at-" is a transfer, its direction only from the first person, the request out of scope), K3-A (the monthly
trend's words show the model that tool alone; code reads the count when the model chooses nothing)."""

from decimal import Decimal

import pytest

from accounts import BALANCE, INCOME, SEARCH, TRANSFERS, Bounds, amount_bounds, tool_of, transfer_direction
from answer_check import fold_turkish
from chat import ToolCall
from grounding import amounts_in
from routing import ACCOUNTS, GENERAL, PLANNING, WEALTH, Route, Router, trend_months
from test_chat import TOKEN, FakeGateway, service

TREND = "get_monthly_trend"


def route(message, previous=None):
    return Router(None).route(message, previous, 30.0)


# --- The amount reader: "binin", "milyonun" (N36) ------------------------------------------------------------------------------

@pytest.mark.parametrize("text, amounts", [
    ("2 binin üstündeki", [Decimal(2000)]), ("5 milyonun altında", [Decimal(5_000_000)]), ("1,5 milyondan fazla", [Decimal(1_500_000)]),
    ("binin üstü", [Decimal(1000)]),
    # A word that only starts like the unit is none: "bina aidatı" is no thousand lira.
    ("bina aidatı", []), ("binici", []),
])
def test_the_genitive_and_ablative_of_a_unit_are_amounts(text, amounts):
    assert amounts_in(text) == amounts


def test_the_limit_of_2_binin_ustundeki_is_two_thousand():
    assert amount_bounds("2 binin üstündeki harcamalarım geçen ay") == Bounds(Decimal(2000), None, "TRY")
    assert tool_of(fold_turkish("2 binin üstündeki harcamalarım geçen ay")) == SEARCH


# --- Transfers: "havale yaptıklarım", "kimlere", "para attım" (N19, N24; K2-A) --------------------------------------------------

@pytest.mark.parametrize("message", ["geçen ay kimlere para attım", "havale yaptıklarım geçen ay", "kimlerden para geldi bu ay",
                                     "geçen ay kimlere para verdim", "kimlerden para aldım",
                                     "bana para atan kişiler", "Hasan para attı mı", "transfer yaptıklarım"])
def test_these_words_name_the_transfers(message):
    assert tool_of(fold_turkish(message)) == TRANSFERS


@pytest.mark.parametrize("message, direction", [
    ("havale yaptıklarım geçen ay", "Sent"), ("transfer yaptığım kişiler", "Sent"), ("havale yaptım mı", "Sent"),
    ("geçen ay kimlere para attım", "Sent"), ("para attıklarım", "Sent"), ("para attığım kişiler", "Sent"),
    ("kimlerden para geldi bu ay", "Received"), ("bana para attı mı", "Received"), ("bana para atan kişiler", "Received"),
    # K2-A: only the first person says who sent it; "Hasan attı mı" could be money to me or to anyone, so every transfer is listed.
    ("Hasan para attı mı", None),
    # "kimlere/kimlerden" alone tell the side ("kime/kimden" do not match them).
    ("geçen ay kimlere transfer var", "Sent"), ("bu ay kimlerden transfer var", "Received"),
])
def test_the_direction_of_the_new_words(message, direction):
    assert transfer_direction(message) == direction


@pytest.mark.parametrize("message", ["Selin'e para at", "Hasan'a 500 tl para atar mısın", "Ece'ye para atabilir misin",
                                     "Annem'e para atayım"])
def test_a_request_to_send_money_with_at_is_out_of_scope(message):
    assert route(message) == Route((), out_of_scope=True)


# --- The balance and the income without the model (K1-C) -----------------------------------------------------------------------

@pytest.mark.parametrize("message", ["elimde ne kadar para var", "Hesapta kalan para ne kadar?", "kartımda ne kadar var",
                                     "hesap durumum nasıl", "Kartımda ne kadar para kaldı?", "hesaplarımda kalan param"])
def test_these_balance_phrasings_need_no_model(message):
    assert route(message) == Route((ACCOUNTS,), only=BALANCE)


@pytest.mark.parametrize("message", ["Geçen ay hesaba ne kadar para yattı", "Kazancım ne kadar bu ay", "Bu ay ne kadar kazandım",
                                     "Hesabıma para yattı mı?"])
def test_these_income_phrasings_need_no_model(message):
    assert route(message) == Route((ACCOUNTS,), only=INCOME)


@pytest.mark.parametrize("message, expected", [
    # A budget's remainder is planning, not the balance.
    ("Bu ay bütçemden kalan para ne kadar?", Route((PLANNING,))),
    # Spending from what is left is still spending.
    ("Kalan paramla bu ay ne harcadım?", Route((GENERAL,))),
    # Depositing ("para yatırdım") is no income, and an investment stays the wealth group's.
    ("Geçen yıl yatırım yapsaydım ne olurdu?", Route((WEALTH,), undecided="neither")),
])
def test_the_new_words_leave_these_alone(message, expected):
    assert route(message) == expected


def test_para_yatirdim_is_no_income():
    assert tool_of(fold_turkish("geçen ay hesaba para yatırdım mı")) != INCOME


# --- The saving advice: "nerelerde" (N70) --------------------------------------------------------------------------------------

@pytest.mark.parametrize("message", ["nerelerde tasarruf ederim geçen ay", "Nelerde tasarruf edebilirim?"])
def test_nerelerde_tasarruf_is_the_advice(message):
    assert route(message).groups == (WEALTH,)


# --- The monthly trend (N57; K3-A) ---------------------------------------------------------------------------------------------

@pytest.mark.parametrize("message", ["son 6 ayda aylık harcamam", "aylara göre harcamalarım son 3 ay", "ay ay harcamalarım",
                                     "Aylık harcamalarım nasıl?",
                                     "Harcamam aylara göre nasıl değişti?",
                                     # CHAT-13 PR2 model run: "her ay" with spending and a counted period is a series of months.
                                     "Son 6 aydır her ay ne kadar harcadım?", "son 3 ayda her ay ne kadar harcamışım"])
def test_the_trend_words_show_only_the_trend(message):
    assert route(message) == Route((GENERAL,), only=TREND)


@pytest.mark.parametrize("message, expected", [
    # N62: "her ay" is the recurring payments' words, not a series of months (no rule; the model's intent question places it).
    ("her ay ödediğim şeyler", Route((GENERAL,), source="fallback")),
    # Without a counted period "her ay" stays as it was: a payment, or a period to be asked.
    ("Her ay ne kadar harcıyorum?", Route((GENERAL,), source="fallback")),
    ("Her ay ne kadar harcadım?", Route((GENERAL,))),
    ("Netflix'e her ay ne kadar ödüyorum?", Route((GENERAL,))),
    # A what-if, a budget and a saving keep their own words.
    ("Aylık harcamam kadar altın alsaydım", Route((WEALTH,), undecided="both")),
    ("Aylık harcamamı %20 azaltırsam", Route((PLANNING,))),
    ("Aylık harcama bütçem 5.000 TL", Route((PLANNING,))),
])
def test_other_monthly_words_keep_their_route(message, expected):
    assert route(message) == expected


TREND_RESULT = {"months": [{"year": 2026, "month": 10, "isPartial": True,
                            "period": {"kind": "Custom", "startDate": "2026-10-01", "endDate": "2026-10-10"}}],
                "merchantMatch": None, "currencies": []}


def test_when_the_model_chooses_nothing_code_reads_the_count():
    chat, selector, gateway, _ = service([], gateway=FakeGateway(TREND_RESULT))

    chat.answer("son 6 ayda aylık harcamam", TOKEN)

    # The model was shown the trend alone and chose nothing; "son 6 ay" is the user's count.
    assert [t["function"]["name"] for t in selector.calls[0][1]] == [TREND]
    assert gateway.calls[0][:2] == (TREND, {"months": 6})


def test_without_a_count_it_is_asked():
    chat, _, gateway, _ = service([], gateway=FakeGateway(TREND_RESULT))

    asked = chat.answer("aylık harcamalarım", TOKEN)
    chat.answer("3", TOKEN)

    assert asked.answer == "Kaç aylık bakayım?"
    assert gateway.calls[0][:2] == (TREND, {"months": 3})


def test_a_count_the_model_made_up_is_asked_too():
    chat, _, gateway, _ = service([ToolCall(TREND, {"months": 6})], gateway=FakeGateway(TREND_RESULT))

    assert chat.answer("aylık harcamalarım", TOKEN).answer == "Kaç aylık bakayım?"
    assert gateway.calls == []


@pytest.mark.parametrize("message, months", [("son bir yılda aylık harcamam", 12), ("aylara göre harcamalarım son 3 ay", 3),
                                             # Past the tool's 12 months, or none: asked, never cut to a count the user did not say.
                                             ("son 2 yılda aylık harcamam", None), ("ay ay harcamalarım", None)])
def test_the_trend_count_is_the_users_within_the_tools_limit(message, months):
    assert trend_months(message) == months
