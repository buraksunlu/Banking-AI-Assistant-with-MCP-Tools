import pytest

from clarification import (PendingStore, ask_amount, ask_amount_again, ask_months, ask_months_again, months_out_of_range, ask_count, ask_currency, ask_merchant, ask_percent, ask_period,
                           ask_complete_month, ask_topic, check_question, choices, chosen_tool, input_hint, resolve, template)
from prompts import build_clarify_messages

# CHAT-03 K3-A/K4-B/K5-A: the question is remembered for one reply, the reply is matched against the stored options in code,
# and a question the model writes is only shown when it names exactly those options.

TOKEN = "secret-token-123"
PERIOD = ask_period("get_spending_summary", {}, "Ne kadar harcadım?")
CITY = ask_merchant("get_merchant_spending", {"period": "Last6Months"}, "şehir", ["Şehir Ulaşım", "Şehirlerarası Otobüs"],
                    "Son 6 ayda şehir'e ne kadar harcadım?")
MONTHS = ask_count("get_monthly_trend", {}, "months", 1, 12, "Harcamam aylara göre nasıl değişti?")


class FakeClock:
    def __init__(self):
        self.now = 1000.0

    def __call__(self):
        return self.now


# The store


def test_a_question_is_kept_for_one_reply():
    store = PendingStore()
    store.put(TOKEN, PERIOD)
    assert store.take(TOKEN) == PERIOD
    assert store.take(TOKEN) is None


def test_it_is_forgotten_after_five_minutes():
    clock = FakeClock()
    store = PendingStore(clock=clock)
    store.put(TOKEN, PERIOD)
    clock.now += 300.1
    assert store.take(TOKEN) is None


def test_another_token_never_sees_it_and_the_token_itself_is_not_kept():
    store = PendingStore()
    store.put(TOKEN, PERIOD)
    assert store.take("another-token") is None
    # Kept under a digest of the token: the AI service never verifies tokens, so a made-up one must not reach someone's question.
    assert TOKEN not in repr(store.__dict__)
    assert store.take(TOKEN) == PERIOD


def test_a_new_question_replaces_the_old_one():
    store = PendingStore()
    store.put(TOKEN, PERIOD)
    store.put(TOKEN, MONTHS)
    assert store.take(TOKEN) == MONTHS


def test_the_store_has_an_upper_bound():
    store = PendingStore(capacity=2)
    for token in ("a", "b", "c"):
        store.put(token, PERIOD)
    assert (store.take("a"), store.take("b"), store.take("c")) == (None, PERIOD, PERIOD)


# Matching the reply


@pytest.mark.parametrize(("reply", "period"), [
    ("2", "LastMonth"), ("2)", "LastMonth"), ("ikincisi", "LastMonth"), ("geçen ay", "LastMonth"), ("ilki", "ThisMonth"),
    ("Son 3 ay", "Last3Months"), ("geçen hafta", "LastWeek"),  # a period of the tools that was not offered still counts
])
def test_a_period_reply(reply, period):
    assert resolve(PERIOD, reply) == {"period": period}


@pytest.mark.parametrize("reply", ["Aboneliklerim neler?", "4", "Eylül", "son 5 ay",
                                   "Bu hafta Starbucks'a ne kadar verdim?"])  # a period inside a question of its own
def test_anything_else_is_a_new_question(reply):
    assert resolve(PERIOD, reply) is None


@pytest.mark.parametrize(("reply", "period"), [("son 3 ay olsun", "Last3Months"), ("Dün ne harcadım?", "Yesterday")])
def test_a_short_reply_may_carry_a_few_more_words(reply, period):
    assert resolve(PERIOD, reply) == {"period": period}


@pytest.mark.parametrize(("reply", "merchant"), [
    ("1", "Şehir Ulaşım"), ("Şehir Ulaşım", "Şehir Ulaşım"), ("şehir ulaşım", "Şehir Ulaşım"), ("ŞEHİRLERARASI", "Şehirlerarası Otobüs"),
    ("ikinci", "Şehirlerarası Otobüs"), ("otobüs", "Şehirlerarası Otobüs"),
])
def test_a_merchant_reply_keeps_the_rest_of_the_question(reply, merchant):
    assert resolve(CITY, reply) == {"period": "Last6Months", "merchant": merchant}


@pytest.mark.parametrize("reply", ["şehir", "3", "Starbucks", "Son 3 ayda Şehir Ulaşım ve Starbucks toplamı ne?"])
def test_a_merchant_reply_that_is_not_exactly_one_option(reply):
    # "şehir" is in both names: choosing one would be the guess the question was asked to avoid.
    assert resolve(CITY, reply) is None


@pytest.mark.parametrize(("reply", "months"), [("6", 6), ("altı", 6), ("son 12 ay", 12), ("1", 1)])
def test_a_count_reply_is_the_count_itself(reply, months):
    # Counts are offered as examples, not numbered, so "1" means one month.
    assert resolve(MONTHS, reply) == {"months": months}


@pytest.mark.parametrize("reply", ["13", "0", "bilmiyorum"])
def test_a_count_outside_the_tools_limits_is_a_new_question(reply):
    assert resolve(MONTHS, reply) is None


# The fixed questions (the fallback of K4-B)


def test_templates_are_only_the_question_the_options_come_under_it():
    # CHAT-10 K5-B, P3-A: the options are rows under the question, so the text no longer lists them.
    assert template(PERIOD) == "Hangi dönem için?"
    assert template(MONTHS) == "Kaç aylık bakayım?"
    assert template(CITY) == "‘şehir’ birden fazla işyeriyle eşleşti. Hangisini kastettiniz?"


# Checking a question the model wrote


@pytest.mark.parametrize("text", [
    "Hangi döneme ait harcamaları öğrenmek istiyorsunuz?",
    "Hangi dönemi görmek istersiniz: bu ay, geçen ay ya da son 3 ay?",     # the options may be named, unnumbered
])
def test_a_written_question_that_only_asks_is_shown(text):
    # CHAT-10 K5-B: the check no longer counts the options (they are rows under the question).
    assert check_question(text, PERIOD)


@pytest.mark.parametrize("text", [
    "Hangi dönem? 1) bu ay 2) geçen ay 3) son 3 ay",                       # the rows again as a numbered list
    "Hangi dönem? Bu ay, geçen ay ya da son 6 ay?",                        # an option added
    "Bu ay için mi bakayım?",                                              # one option named, the others not
    "Hangi döneme bakmamı istersiniz? - Bu ay - Geçen ay - Son 3 ay",       # the rows again as a list (clarify-v3 trial)
    "Hangi döneme bakmak istediğinizi belirtir misiniz? Bu ay; Geçen ay; Son 3 ay",
    "Hangi dönem?\nBu ay\nGeçen ay\nSon 3 ay",
    "Bu hafta mı, geçen ay mı?",                                           # a period that is not an option
    "Bu ay mı, geçen ay mı, son 3 ay mı yoksa bu hafta mı?",               # every option named, plus one that is not offered
    "Bu ay 735,00 TL harcadınız. Başka bir dönem mi? 1) bu ay 2) geçen ay 3) son 3 ay",  # an answer with a figure
    "1) bu ay 2) geçen ay 3) son 3 ay",                                                     # only the list, no question
    "Dönemi lütfen öğreniriz. 1) bu ay 2) geçen ay 3) son 3 ay",                           # a statement, not a question
    "",
])
def test_a_written_question_that_changes_the_options_is_not_shown(text):
    assert not check_question(text, PERIOD)


def test_a_written_count_question_uses_the_examples():
    assert check_question("Kaç aylık bir dönemi incelemek istersiniz, örneğin son 3, 6 ya da 12 ay?", MONTHS)
    assert not check_question("Kaç aylık? Son 3, 6 ya da 24 ay olabilir.", MONTHS)


def test_a_written_merchant_question_may_repeat_the_users_words():
    assert check_question("‘şehir’ derken hangi işyerini kastettiniz?", CITY)
    assert not check_question("Hangi işyerini kastettiniz? 1. Şehir Ulaşım 2. Şehirlerarası Otobüs", CITY)
    assert not check_question("Şehir Ulaşım'ı mı kastettiniz?", CITY)
    # The clarify-v3 trial asked which city: a merchant question must say it is about a merchant.
    assert not check_question("Hangi şehir için harcamalarınızı incelememi istiyorsunuz?", CITY)


# The prompt for the written question


def test_the_prompt_gives_the_options_and_cannot_be_escaped():
    pending = ask_period("get_spending_summary", {}, "Ne kadar <harcadım>?")
    system, user = build_clarify_messages(pending.question, pending.kind, [o.label for o in pending.options])
    assert system["role"] == "system" and user["role"] == "user"
    # clarify-v3 trial: given the periods, the model copied them as a list; it is told only what is missing.
    assert "geçen ay" not in user["content"] and "dönem" in user["content"]
    assert "numaralandırma" in system["content"] and "altında" in system["content"]
    assert "<harcadım>" not in user["content"] and "‹harcadım›" in user["content"]


def test_the_merchant_prompt_names_the_users_words():
    system, user = build_clarify_messages(CITY.question, CITY.kind, [o.label for o in CITY.options], asked="<şehir>")
    assert "‘‹şehir›’ adı birden fazla işyerine uyuyor" in user["content"] and "Şehir Ulaşım" not in user["content"]
    assert "Eksik bilgi" not in user["content"]


# MCP-17/18: the budget, its currency and the cut. The options are fixed and the questions are never the model's wording.

AMOUNT = ask_amount("check_budget", {"categoryCode": "FOOD"}, "Bu ay yeme içme bütçemi aşar mıyım?", later=("currencyCode",))
CURRENCY = ask_currency("check_budget", {"amount": 500}, "Bu ay 500 bütçem var, ne durumdayım?")
PERCENT = ask_percent("simulate_savings", {"merchant": "Starbucks"}, "Starbucks'tan ne kadar tasarruf ederim?")


@pytest.mark.parametrize(("reply", "completed"), [
    ("3000 TL", {"categoryCode": "FOOD", "amount": 3000, "currencyCode": "TRY"}),    # the currency asked next comes along
    ("3.000", {"categoryCode": "FOOD", "amount": 3000}),
    ("2.500,50 lira", {"categoryCode": "FOOD", "amount": 2500.5, "currencyCode": "TRY"}),
    ("5 bin", {"categoryCode": "FOOD", "amount": 5000}),
    ("5k", {"categoryCode": "FOOD", "amount": 5000}),                                 # CHAT-10 K9-C: was 5
    ("10k TL", {"categoryCode": "FOOD", "amount": 10000, "currencyCode": "TRY"}),
])
def test_an_amount_reply(reply, completed):
    assert resolve(AMOUNT, reply) == completed


@pytest.mark.parametrize("reply", ["bilmiyorum", "0", "Bu ay market için ne kadar harcadım acaba?", "5x"])
def test_a_reply_without_an_amount_is_a_new_question(reply):
    assert resolve(AMOUNT, reply) is None


@pytest.mark.parametrize(("reply", "currency"), [("1", "TRY"), ("TL", "TRY"), ("euro", "EUR"), ("2", "EUR"), ("Dolar", "USD"),
                                                 ("4", "GBP"), ("lira olsun", "TRY")])
def test_a_currency_reply(reply, currency):
    assert resolve(CURRENCY, reply) == {"amount": 500, "currencyCode": currency}


@pytest.mark.parametrize("reply", ["5", "yen", "bilmiyorum"])
def test_a_reply_that_is_not_one_of_the_currencies(reply):
    assert resolve(CURRENCY, reply) is None


@pytest.mark.parametrize(("reply", "value"), [("%20", 20), ("20", 20), ("yüzde 30", 30), ("yarısı", 50), ("100", 100)])
def test_a_cut_reply(reply, value):
    assert resolve(PERCENT, reply) == {"merchant": "Starbucks", "reductionPercent": value}


@pytest.mark.parametrize("reply", ["150", "0", "biraz"])
def test_a_cut_outside_1_to_100_or_without_a_figure_is_a_new_question(reply):
    assert resolve(PERCENT, reply) is None


def test_the_figure_questions_are_fixed():
    assert template(AMOUNT) == "Bütçeniz ne kadar?"
    assert template(CURRENCY) == "Bütçeniz hangi para biriminde?"
    assert template(PERCENT) == "Ne kadar azaltmayı düşünüyorsunuz?"
    # The model words only the period, count and merchant questions (CHAT-03 K4-B); these three are always the fixed text.
    assert [p.model_worded for p in (AMOUNT, CURRENCY, PERCENT, PERIOD, MONTHS, CITY)] == [False, False, False, True, True, True]


# CHAT-09 PR3: which of two planning calculations the user meant (K19-C), worded by the model in the user's words (K20-B). The
# "what if" scenario question (K17-C) left in CHAT-10 PR3b D8-B: an undecided "what if" is the monthly saving.

TOPIC = ask_topic("Bütçemi kısıp tasarruf etsem?")
TOPIC_TEXT = "Bunu iki şekilde hesaplayabilirim. Hangisini istersiniz?"


def test_the_choice_question_waits_for_a_tool_and_keeps_the_users_question():
    assert (TOPIC.tool, TOPIC.kind, TOPIC.original) == ("", "topic", "Bütçemi kısıp tasarruf etsem?")
    assert TOPIC.model_worded


def test_the_fixed_choice_question():
    assert template(TOPIC) == TOPIC_TEXT


@pytest.mark.parametrize(("reply", "tool"), [
    ("1", "check_budget"), ("bütçe", "check_budget"), ("bütçe kontrolü", "check_budget"), ("limit", "check_budget"),
    ("2", "simulate_savings"), ("tasarruf", "simulate_savings"), ("azaltmak", "simulate_savings"),
])
def test_a_topic_reply(reply, tool):
    assert chosen_tool(TOPIC, reply) == tool


@pytest.mark.parametrize("reply", ["3", "bilmem", "ikisi de", "bütçe ve tasarruf", "Bu ay ne kadar harcadım, söyler misin?"])
def test_a_topic_reply_that_is_not_one_choice_is_a_new_question(reply):
    # Naming both would be the guess the question was asked to avoid; a longer message is a question of its own.
    assert chosen_tool(TOPIC, reply) is None


def test_a_choice_is_not_a_figure_reply():
    assert chosen_tool(PERIOD, "1") is None and resolve(TOPIC, "1") is None


@pytest.mark.parametrize("text", [
    "Bütçe kontrolü mü, yoksa tasarruf hesabı mı yapayım?",
    "Bütçenizi aşıp aşmayacağınıza mı bakayım, yoksa ne kadar biriktireceğinize mi?",
    "Bunu nasıl hesaplayayım?",                                            # CHAT-10 K5-B: the rows name the choices
])
def test_a_written_choice_question_may_use_the_users_words(text):
    # K20-B: any of an option's words counts as naming it, so the model can follow the user's phrasing.
    assert check_question(text, TOPIC)


@pytest.mark.parametrize("text", [
    "Hangisini istersiniz: 1) bütçe kontrolü 2) tasarruf hesabı?",                 # the rows again as a numbered list
    "Bütçe mi, tasarruf mu, yoksa 3) yatırım mı?",                                 # a choice added
    "Bütçeniz 1.000 TL mi, yoksa tasarruf mu?",                                    # a figure the user did not say
    "Bütçe kontrolü ya da tasarruf hesabı.",                                       # no question
])
def test_a_written_choice_question_that_changes_the_choices_is_not_shown(text):
    assert not check_question(text, TOPIC)


def test_the_prompt_describes_both_calculations_and_asks_for_the_users_words():
    system, user = build_clarify_messages(TOPIC.question, TOPIC.kind, [o.label for o in TOPIC.options])
    assert "bütçe kontrolü; tasarruf hesabı" in user["content"]
    assert "kendi sözleri" in user["content"]


# CHAT-10 K9-C: an amount the code cannot read is asked again, saying which words were not understood.
def test_an_unread_amount_is_asked_again_with_what_was_not_understood():
    again = ask_amount_again(AMOUNT, "5x")
    assert template(again) == "‘5x’ tutarını anlayamadım. Bütçeniz ne kadar?"
    assert choices(again) == choices(AMOUNT)
    assert (again.tool, again.arguments, again.later) == (AMOUNT.tool, AMOUNT.arguments, AMOUNT.later)
    assert not again.model_worded


def test_an_unread_monthly_amount_is_asked_again_in_its_own_words():
    monthly = ask_amount("what_if_monthly", {}, "Her ay biriktirseydim ne olurdu?")
    assert template(ask_amount_again(monthly, "2y")) == "‘2y’ tutarını anlayamadım. Ayda kaç TL biriktirirdiniz?"


# CHAT-10 K10-A: a failed turn gives its question back, but never over a question the same turn already asked.
def test_a_question_is_given_back_only_when_nothing_newer_waits():
    store = PendingStore()
    store.put_back("t", AMOUNT)
    assert store.take("t") == AMOUNT
    store.put("t", CURRENCY)
    store.put_back("t", AMOUNT)
    assert store.take("t") == CURRENCY


# CHAT-10 PR3: the options as tappable rows (K4-A), every one of them (P1-D), each sending its label (K6-A)

MONTHLY = ask_amount("what_if_monthly", {}, "Her ay biriktirseydim ne olurdu?", later=("months",))
DURATION = ask_months("what_if_monthly", {"amount": 1000, "currencyCode": "TRY"}, "Ayda 1.000 TL biriktirseydim ne olurdu?")
LAST_MONTH = ask_complete_month("get_income_share", {}, "Maaşımın ne kadarını harcadım?", today=__import__("datetime").date(2026, 11, 10))


def test_every_question_offers_its_options_as_choices():
    assert [c.label for c in choices(PERIOD)] == ["bu ay", "geçen ay", "son 3 ay"]
    assert [c.label for c in choices(MONTHS)] == ["son 3 ay", "son 6 ay", "son 12 ay"]
    assert [c.label for c in choices(CITY)] == ["Şehir Ulaşım", "Şehirlerarası Otobüs"]
    assert [c.label for c in choices(CURRENCY)] == ["TL", "Euro", "Dolar", "Sterlin"]
    assert [c.label for c in choices(PERCENT)] == ["%10", "%25", "%50"]
    # P1-D: the amount examples are choices too; a budget's says its currency, so one tap answers both questions.
    # PR3b D7-A (simulator trial, 12 Nov 2026): one set of examples for every amount question.
    assert [c.label for c in choices(AMOUNT)] == ["1.000 TL", "5.000 TL", "10.000 TL"]
    assert [c.label for c in choices(MONTHLY)] == ["1.000 TL", "5.000 TL", "10.000 TL"]
    assert [c.label for c in choices(LAST_MONTH)] == ["geçen ay (Ekim)"]
    # INV-03 K4-A: a saving's duration, said the way people say it (not "son 3 ay", which is a period).
    assert [c.label for c in choices(DURATION)] == ["3 ay", "6 ay", "12 ay"]


def test_only_the_two_calculations_carry_a_detail():
    # P5-A: "tek seferlik" alone does not say what it is.
    assert [c.detail for c in choices(TOPIC)] == [o.detail for o in TOPIC.options]
    assert all(c.detail for c in choices(TOPIC))
    assert all(c.detail == "" for p in (PERIOD, MONTHS, CITY, CURRENCY, PERCENT, AMOUNT) for c in choices(p))


@pytest.mark.parametrize("pending", [PERIOD, MONTHS, CITY, CURRENCY, PERCENT, AMOUNT, MONTHLY, LAST_MONTH, DURATION],
                         ids=["period", "count", "merchant", "currency", "percent", "amount", "monthly", "complete-month", "duration"])
def test_tapping_a_choice_sends_its_label_and_the_code_reads_it_as_that_option(pending):
    # K6-A: the app sends the label as written; each one must complete the call with the option's own value.
    for option, choice in zip(pending.options, choices(pending), strict=True):
        assert resolve(pending, choice.label)[pending.argument] == option.value


def test_a_budget_example_answers_its_currency_too():
    assert resolve(AMOUNT, "5.000 TL") == {"categoryCode": "FOOD", "amount": 5000, "currencyCode": "TRY"}
    assert resolve(MONTHLY, "10.000 TL") == {"amount": 10000, "currencyCode": "TRY"}


def test_a_monthly_amount_reply_that_also_says_the_duration_answers_both():
    # Like "3000 TL" answering a budget's currency: "5 bin 6 ay" leaves nothing to ask next.
    assert resolve(MONTHLY, "5 bin 6 ay") == {"amount": 5000, "currencyCode": "TRY", "months": 6}
    assert resolve(MONTHLY, "5 bin 30 ay") == {"amount": 5000, "currencyCode": "TRY"}   # out of range: asked next, with its note


def test_only_an_amount_question_invites_an_amount_of_ones_own():
    # PR3b D6-A: the app draws this as the card's last row, which opens the keyboard instead of sending anything.
    assert input_hint(AMOUNT) == input_hint(MONTHLY) == "Başka bir tutar yazın"
    assert input_hint(DURATION) == "Başka bir süre yazın (2–22 ay)"
    assert all(input_hint(p) == "" for p in (PERIOD, MONTHS, CITY, CURRENCY, PERCENT, LAST_MONTH, TOPIC))


@pytest.mark.parametrize("pending", [TOPIC], ids=["topic"])
def test_tapping_a_calculation_chooses_its_tool(pending):
    for option, choice in zip(pending.options, choices(pending), strict=True):
        assert chosen_tool(pending, choice.label) == option.value


# --- INV-03 PR2: the saving's duration (K2-A asked when unsaid, K4-A examples + one's own, K5-A ay/yıl/buçuk, K6-A limits) -----

@pytest.mark.parametrize(("reply", "months"), [
    ("3 ay", 3), ("6 ay", 6), ("12 ay", 12), ("8", 8), ("8 ay olsun", 8), ("bir yıl", 12), ("1,5 yıl", 18), ("yarım yıl", 6),
    ("22 ay", 22), ("2 ay", 2),
])
def test_a_duration_reply(reply, months):
    assert resolve(DURATION, reply) == {"amount": 1000, "currencyCode": "TRY", "months": months}


@pytest.mark.parametrize("reply", ["1 ay", "23 ay", "2 yıl", "6 ay ya da 1 yıl", "2,5 ay", "Geçen ay ne harcadım?"])
def test_a_reply_without_one_duration_in_range_completes_nothing(reply):
    assert resolve(DURATION, reply) is None


@pytest.mark.parametrize(("reply", "out"), [("1 ay", True), ("2 yıl", True), ("30", True), ("6 ay", False), ("merhaba", False),
                                            ("6 ay ya da 1 yıl", False), ("Son 30 günde ne harcadım?", False)])
def test_a_short_duration_out_of_range_is_told(reply, out):
    assert months_out_of_range(DURATION, reply) is out


def test_the_duration_question_is_the_fixed_text_with_the_limits_when_one_was_out():
    assert template(DURATION) == "Kaç ay boyunca biriktirirdiniz?"
    assert not DURATION.model_worded
    again = ask_months_again(DURATION)
    assert template(again) == "En az 2, en fazla 22 ay için hesaplayabilirim. Kaç ay boyunca biriktirirdiniz?"
    assert again.arguments == DURATION.arguments and again.options == DURATION.options


def test_the_duration_limits_are_the_servers_own():
    # INV-03 K6-A: the note names the limits, so they must be the ones the MCP server publishes (WhatIfMath, [Range(2, 22)]).
    import json
    from pathlib import Path

    from clarification import MAXIMUM_MONTHS, MINIMUM_MONTHS
    tools = json.loads((Path(__file__).parent.parent / "model_check" / "mcp_tools.json").read_text("utf-8"))["tools"]
    months = next(t for t in tools if t["function"]["name"] == "what_if_monthly")["function"]["parameters"]["properties"]["months"]
    assert (months["minimum"], months["maximum"]) == (MINIMUM_MONTHS, MAXIMUM_MONTHS)
