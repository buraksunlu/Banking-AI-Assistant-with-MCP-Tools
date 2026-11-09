"""CHAT-09 PR2 K11-A: a question typed without Turkish letters ("Dun ne kadar harcadim") reads like the one with them.

One fold for every rule: ``fold_turkish`` turns the six Turkish letters into plain ones, and every rule pattern is folded the same way
when it is compiled (``fold_rule``), so the rules stay written in Turkish. PR1 measured 40 phrasings that lost their period, group or
figure without the letters. Expected values by hand.
"""

import re

import pytest

import answer_check
import clarification
import grounding
import routing
import wealth
from answer_check import fold_rule, fold_turkish, has_advice
from clarification import ask_complete_month, resolve
from conversation import LastCall
from grounding import (amount_currency_in, currency_in, mentions_category, mentions_count, mentions_period, percent_in,
                       period_in)
from routing import GENERAL, PLANNING, WEALTH, Route, Router

ASCII = str.maketrans("çğıöşüÇĞİÖŞÜ", "cgiosuCGIOSU")


def plain(text: str) -> str:
    return text.translate(ASCII)


@pytest.mark.parametrize(("text", "folded"), [
    ("Dün GEÇEN Şubat", "dun gecen subat"),
    ("İŞYERİ ılık ÖĞÜN Çay", "isyeri ilik ogun cay"),
    ("TALIMAT talımat TALİMAT", "talimat talimat talimat"),
])
def test_the_fold_turns_every_turkish_letter_into_a_plain_one(text, folded):
    assert fold_turkish(text) == folded


def test_a_rule_pattern_is_folded_the_same_way():
    assert fold_rule(r"\bdün|geçen\s+ay|[a-zçğöşü]+") == r"\bdun|gecen\s+ay|[a-zcgosu]+"


def test_no_rule_table_keeps_a_turkish_letter_that_folded_text_can_never_match():
    # A pattern written with "ş" would silently never match again: every compiled rule and every word table must be folded.
    letters = re.compile("[çğıöşü]")
    for module in (answer_check, clarification, grounding, routing, wealth):
        for name, value in vars(module).items():
            patterns = [value.pattern] if isinstance(value, re.Pattern) else []
            if isinstance(value, dict):
                patterns = [k for k in value if isinstance(k, str)] + [v for v in value.values() if isinstance(v, str)]
            if isinstance(value, (list, tuple)):
                patterns = [p for item in value if isinstance(item, tuple) for p in item if isinstance(p, str)]
            leaking = [p for p in patterns if letters.search(p)]
            assert not leaking or name in ("CURRENCY_OPTIONS", "PERIOD_OPTIONS", "GROUP_LABELS"), (module.__name__, name, leaking)


@pytest.mark.parametrize(("message", "groups"), [
    ("Dün ne harcadım?", (GENERAL,)),
    ("Geçen ay işyerlerine göre harcamam?", (GENERAL,)),
    ("Önümüzdeki 7 günde ödemelerim?", (GENERAL,)),
    ("Bu ay 1.000 TL bütçem var, ne durumdayım?", (PLANNING,)),
    ("Market harcamamı çeyrek azaltsam?", (PLANNING,)),
    ("Geçen ay maaşımın ne kadarını harcadım?", (WEALTH,)),
    ("Geçen ay için tasarruf önerin var mı?", (WEALTH,)),
])
def test_the_word_rules_choose_the_same_group_without_the_letters(message, groups):
    router = Router()
    assert router.route(plain(message), None, 30.0) == router.route(message, None, 30.0) == Route(groups, source="rule")


@pytest.mark.parametrize("message", ["Dolar yükselir mi?", "Euro düşer mi?", "Bir şaka yap", "Anneme para gönder"])
def test_advice_and_out_of_scope_rules_hold_without_the_letters(message):
    assert Router().route(plain(message), None, 30.0) == Router().route(message, None, 30.0)


@pytest.mark.parametrize(("text", "period"), [
    ("dün", "Yesterday"), ("bugün", "Today"), ("geçen hafta", "LastWeek"), ("geçen ay", "LastMonth"), ("son üç ay", "Last3Months"),
    ("son altı ay", "Last6Months"),
])
def test_periods_are_read_without_the_letters(text, period):
    assert period_in(plain(text)) == period_in(text) == period


@pytest.mark.parametrize("message", ["Dün ne harcadım?", "Ağustos'ta ne harcadım?", "Son 7 günde?", "Şubat ayında?"])
def test_a_period_is_noticed_without_the_letters(message):
    assert mentions_period(plain(message))


def test_figures_and_categories_are_read_without_the_letters():
    assert percent_in("yuzde 30") == percent_in("yüzde 30") == 30
    assert percent_in("yuzde yirmi bes") == 25
    assert percent_in("ceyrek") == 25
    assert mentions_count("son alti ay", 6) and mentions_count("gecen yil", 12)
    assert mentions_category("eglence harcamami", "ENTERTAINMENT") and mentions_category("ulasim", "TRANSPORTATION")
    assert mentions_category("saglik", "HEALTH") and mentions_category("dogalgaz faturasi", "BILLS")
    assert currency_in("100 tl") == "TRY" and amount_currency_in("1.000 TL'yi dolar olarak") == "TRY"
    # A word that holds the letters of a code is still no currency ("atletizm" is no "tl").
    assert currency_in("atletizm") is None


def test_the_advised_amount_and_its_follow_up_are_read_without_the_letters():
    assert wealth.advised_saving_call("Onerdigin tutari biriktirseydim ne olurdu?", None) == ("what_if_monthly", {"fromAdvice": True})
    advice = LastCall("get_spending_advice", {"period": "LastMonth"}, "Gecen ay nerede tasarruf edebilirim?")
    assert wealth.advised_saving_call("Peki bu tutari biriktirseydim?", advice) == ("what_if_monthly", {"fromAdvice": True})


def test_a_reply_to_a_question_is_read_without_the_letters():
    pending = clarification.ask_period("get_spending_summary", {}, "Ne kadar harcadım?")
    assert resolve(pending, "gecen ay") == {"period": "LastMonth"}
    assert resolve(pending, "ucuncu") == {"period": "Last3Months"}
    month = ask_complete_month("get_income_share", {}, "Maaşımın ne kadarını harcadım?")
    assert resolve(month, "GEÇEN AY") == {"period": "LastMonth"}


def test_an_answer_that_sounds_like_advice_is_found_without_the_letters():
    assert has_advice("Dolar yukselecek, simdi almalisiniz.")
    assert has_advice("Altın düşecek.") and has_advice("Altin dusecek.")
