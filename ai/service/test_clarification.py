import pytest

from clarification import (PendingStore, ask_amount, ask_amount_again, ask_count, ask_currency, ask_merchant, ask_percent, ask_period, ask_scenario,
                           ask_topic, check_question, chosen_tool, resolve, template)
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


def test_templates_name_the_stored_options():
    assert template(PERIOD) == "Hangi dönem için? 1) bu ay 2) geçen ay 3) son 3 ay"
    assert template(MONTHS) == "Kaç aylık bakayım? Örneğin son 3, 6 ya da 12 ay."
    assert template(CITY) == "‘şehir’ birden fazla işyeriyle eşleşti: 1) Şehir Ulaşım 2) Şehirlerarası Otobüs. Hangisini kastettiniz?"


# Checking a question the model wrote


@pytest.mark.parametrize("text", [
    "Hangi döneme ait harcamaları öğrenmek istiyorsunuz?\n1. Bu ay\n2. Geçen ay\n3. Son 3 ay",
    "Hangi dönemi görmek istersiniz: 1) bu ay, 2) geçen ay ya da 3) son 3 ay?",
])
def test_a_written_question_with_exactly_the_options_is_shown(text):
    assert check_question(text, PERIOD)


@pytest.mark.parametrize("text", [
    "Hangi dönem? 1) bu ay 2) geçen ay",                                   # an option left out
    "Hangi dönem? 1) bu ay 2) geçen ay 3) son 3 ay 4) son 6 ay",           # an option added
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
    assert check_question("Hangi işyerini kastettiniz? 1. Şehir Ulaşım 2. Şehirlerarası Otobüs", CITY)
    assert not check_question("Hangi işyerini kastettiniz? 1. Şehir Ulaşım", CITY)


# The prompt for the written question


def test_the_prompt_gives_the_options_and_cannot_be_escaped():
    pending = ask_period("get_spending_summary", {}, "Ne kadar <harcadım>?")
    system, user = build_clarify_messages(pending.question, pending.kind, [o.label for o in pending.options], numbered=True)
    assert system["role"] == "system" and user["role"] == "user"
    assert "bu ay; geçen ay; son 3 ay" in user["content"]
    assert "<harcadım>" not in user["content"] and "‹harcadım›" in user["content"]


def test_the_merchant_prompt_names_the_users_words():
    system, user = build_clarify_messages(CITY.question, CITY.kind, [o.label for o in CITY.options], numbered=True, asked="<şehir>")
    assert "‘‹şehir›’ adı birden fazla işyerine uyuyor" in user["content"]
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
    assert template(AMOUNT) == "Bütçeniz ne kadar? Örneğin 3.000 TL."
    assert template(CURRENCY) == "Bütçeniz hangi para biriminde? 1) TL 2) Euro 3) Dolar 4) Sterlin"
    assert template(PERCENT) == "Ne kadar azaltmayı düşünüyorsunuz? Örneğin %10, %25 ya da %50."
    # The model words only the period, count and merchant questions (CHAT-03 K4-B); these three are always the fixed text.
    assert [p.model_worded for p in (AMOUNT, CURRENCY, PERCENT, PERIOD, MONTHS, CITY)] == [False, False, False, True, True, True]


# CHAT-09 PR3: which of two calculations the user meant (K17-C, K19-C), worded by the model in the user's words (K20-B)

SCENARIO = ask_scenario("Biriktirseydim ne olurdu?")
TOPIC = ask_topic("Bütçemi kısıp tasarruf etsem?")
SCENARIO_TEXT = ("Bunu iki şekilde hesaplayabilirim: 1) tek seferlik (bir dönemdeki harcamanızla o dönemin başında dolar, euro ya "
                 "da altın alsaydınız) 2) aylık birikim (son 12 ay boyunca her ay belirli bir TL tutarı biriktirseydiniz). "
                 "Hangisini istersiniz?")
TOPIC_TEXT = ("Bunu iki şekilde hesaplayabilirim: 1) bütçe kontrolü (bu ayki harcamanız söylediğiniz bütçeyi aşar mı) 2) tasarruf "
              "hesabı (bir harcamayı azaltırsanız ne kadar biriktirirsiniz). Hangisini istersiniz?")


def test_the_choice_questions_wait_for_a_tool_and_keep_the_users_question():
    assert (SCENARIO.tool, SCENARIO.kind, SCENARIO.original) == ("", "scenario", "Biriktirseydim ne olurdu?")
    assert (TOPIC.tool, TOPIC.kind) == ("", "topic")
    assert SCENARIO.numbered and SCENARIO.model_worded and TOPIC.model_worded


def test_the_fixed_choice_questions():
    assert template(SCENARIO) == SCENARIO_TEXT
    assert template(TOPIC) == TOPIC_TEXT


@pytest.mark.parametrize(("reply", "tool"), [
    ("1", "what_if_one_off"), ("ilki", "what_if_one_off"), ("tek seferlik", "what_if_one_off"), ("Bir kerede", "what_if_one_off"),
    ("TEK SEFERDE", "what_if_one_off"), ("2", "what_if_monthly"), ("ikincisi", "what_if_monthly"), ("aylık", "what_if_monthly"),
    ("her ay", "what_if_monthly"), ("her ay 2 bin", "what_if_monthly"), ("düzenli biriktirseydim", "what_if_monthly"),
])
def test_a_scenario_reply_in_the_users_own_words(reply, tool):
    assert chosen_tool(SCENARIO, reply) == tool


@pytest.mark.parametrize("reply", ["3", "bilmem", "ikisi de", "tek seferlik, aylık", "tek seferlik değil her ay",
                                   "Her ay ne kadar harcadığımı söyler misin?"])
def test_a_scenario_reply_that_is_not_one_choice_is_a_new_question(reply):
    # "tek seferlik, aylık" names both: choosing one would be the guess the question was asked to avoid. A longer message that
    # happens to say "her ay" is a question of its own.
    assert chosen_tool(SCENARIO, reply) is None


@pytest.mark.parametrize(("reply", "tool"), [
    ("1", "check_budget"), ("bütçe", "check_budget"), ("bütçe kontrolü", "check_budget"), ("limit", "check_budget"),
    ("2", "simulate_savings"), ("tasarruf", "simulate_savings"), ("azaltmak", "simulate_savings"),
])
def test_a_topic_reply(reply, tool):
    assert chosen_tool(TOPIC, reply) == tool


def test_a_choice_is_not_a_figure_reply():
    assert chosen_tool(PERIOD, "1") is None and resolve(SCENARIO, "1") is None


@pytest.mark.parametrize("text", [
    "Bunu tek seferlik mi, yoksa aylık birikim olarak mı hesaplayayım?",
    "Geçen ayki harcamanızla bir kerede mi dolar alsaydınız, yoksa her ay düzenli olarak mı biriktirseydiniz?",
    "Hangisini istersiniz: 1) tek seferlik 2) son 12 ay boyunca aylık birikim?",
])
def test_a_written_choice_question_may_use_the_users_words(text):
    # K20-B: any of an option's words counts as naming it, so the model can follow the user's phrasing.
    assert check_question(text, SCENARIO)


@pytest.mark.parametrize("text", [
    "Tek seferlik mi hesaplayayım?",                                               # the monthly saving left out
    "Tek seferlik mi, aylık mı, yoksa 3) yıllık mı?",                              # a choice added
    "Her ay 1.000 TL mi biriktirirdiniz, yoksa tek seferlik mi?",                  # a figure the user did not say
    "Tek seferlik ya da aylık birikim.",                                           # no question
])
def test_a_written_choice_question_that_changes_the_choices_is_not_shown(text):
    assert not check_question(text, SCENARIO)


def test_the_prompt_describes_both_calculations_and_asks_for_the_users_words():
    system, user = build_clarify_messages(SCENARIO.question, SCENARIO.kind, [o.label for o in SCENARIO.options], numbered=True)
    assert "tek seferlik; aylık birikim" in user["content"]
    assert "son 12 ay" in user["content"] and "kendi sözleri" in user["content"]


def test_a_short_spending_question_is_not_the_one_off_choice():
    assert chosen_tool(SCENARIO, "Dün ne harcadım?") is None


# CHAT-10 K9-C: an amount the code cannot read is asked again, saying which words were not understood.
def test_an_unread_amount_is_asked_again_with_what_was_not_understood():
    again = ask_amount_again(AMOUNT, "5x")
    assert template(again) == "‘5x’ tutarını anlayamadım. Bütçeniz ne kadar? Örneğin 3.000 TL."
    assert (again.tool, again.arguments, again.later) == (AMOUNT.tool, AMOUNT.arguments, AMOUNT.later)
    assert not again.model_worded


def test_an_unread_monthly_amount_is_asked_again_in_its_own_words():
    monthly = ask_amount("what_if_monthly", {}, "Her ay biriktirseydim ne olurdu?")
    assert template(ask_amount_again(monthly, "2y")) == "‘2y’ tutarını anlayamadım. Ayda kaç TL biriktirirdiniz? Örneğin 1.000."


# CHAT-10 K10-A: a failed turn gives its question back, but never over a question the same turn already asked.
def test_a_question_is_given_back_only_when_nothing_newer_waits():
    store = PendingStore()
    store.put_back("t", AMOUNT)
    assert store.take("t") == AMOUNT
    store.put("t", CURRENCY)
    store.put_back("t", AMOUNT)
    assert store.take("t") == CURRENCY
