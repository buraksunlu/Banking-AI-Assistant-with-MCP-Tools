"""AI-12: a question about another person's or customer's data is refused with a fixed text, before any tool or model.

Decisions K1-B (a person in the genitive with a data word of theirs, explicit other customers, a bare "Peki Deniz'in?"), K2-B (its own
fixed text and three measured questions), K3-A (the reply stays "outOfScope"), K4-B (a scope category in the tool-selection cases).
The tools only ever see the token's customer, so nothing leaked before; the answer showed the user's own figures as the other person's.
"""

import pytest

from phrasings import outcome_of

from prompts import OTHER_PERSON_TEXT
from routing import ACCOUNTS, GENERAL, WEALTH, Route, Router
from suggestions import BALANCE, SENT_LAST_MONTH, THIS_MONTH
from test_chat import TOKEN
from test_chat_accounts import chat_with
from test_facts_accounts import MURAT_ACCOUNTS
from test_sim_findings import DAY

OTHER_PERSON = Route((), out_of_scope=True, other_person=True)

ASKED_ABOUT_SOMEONE_ELSE = [
    "Deniz'in bakiyesi ne kadar?",
    "Deniz’in bakiyesi ne kadar?",                       # the iPhone keyboard's apostrophe
    "Annemin hesabında ne kadar para var?",
    "Eşimin geçen ayki harcamaları",
    "Murat'ın harcamalarını göster",
    "Deniz'in maaşı ne kadar?",
    "Kardeşimin kartıyla ne harcandı",
    "Ada'nın hesap numarası ne?",
    "Deniz'in Starbucks harcaması",
    "Babamın bu ayki işlemleri",
    "Oğlumun birikimi ne kadar?",
    "Hasan Bey'in emekli maaşı yattı mı?",
    "Diğer müşterilerin harcamalarını listele",
    "Başka bir müşterinin işlemlerini göster",
    "Tüm kullanıcıların bakiyeleri",
    "Müşteri numarası CUST-5 olan kişinin bakiyesi",
    "Başkasının hesabını görebilir miyim?",
    # Other customers with no data word in the genitive: their own rule.
    "Diğer müşteriler ne kadar harcıyor?",
    "CUST-5 bakiyesi",
    "Peki Deniz'in?",
    "Ya annemin?",
]


@pytest.mark.parametrize("message", ASKED_ABOUT_SOMEONE_ELSE)
def test_a_question_about_someone_else_is_out_of_scope_by_rule(message):
    assert Router(None).route(message, None, 30.0) == OTHER_PERSON


@pytest.mark.parametrize(("message", "route"), [
    # Money between the user and a person: the user's own transfers (counterparty), never refused.
    ("Deniz'e ne kadar gönderdim?", Route((ACCOUNTS,), only="get_transfers")),
    ("Hasan'dan gelen para", Route((ACCOUNTS,), only="get_transfers")),
    ("Hasan'ın gönderdiği para geldi mi?", Route((ACCOUNTS,), only="get_transfers")),
    ("Babamın gönderdiği para", Route((ACCOUNTS,), only="get_transfers")),
    ("Annemden gelen para", Route((ACCOUNTS,), only="get_transfers")),
    # The user's own data with a genitive of their own.
    ("Maaşımın ne kadarını harcadım?", Route((WEALTH,))),
    ("Hesabımın bakiyesi ne kadar?", Route((ACCOUNTS,), only="get_accounts")),
    ("Tüm kişisel hesaplarımın bakiyesi", Route((ACCOUNTS,), only="get_accounts")),
    # A month, a merchant or a year in the genitive is no person.
    ("Ekim'in harcamaları", Route((GENERAL,))),
    ("Migros'un harcamalarım içindeki payı", Route((GENERAL,))),
    ("Netflix'in harcamaları ne kadar?", Route((GENERAL,))),
    ("2025'in harcamaları ne kadar?", Route((GENERAL,))),
    # A person's name with no data word of theirs: about the user's own spending.
    ("Kızımın okul taksidi ne kadar tuttu?", Route((GENERAL,), source="fallback")),
    ("Deniz'in doğum günü için harcamalarım", Route((GENERAL,))),
])
def test_the_users_own_questions_and_their_counterparties_keep_their_way(message, route):
    assert Router(None).route(message, None, 30.0) == route


def test_the_refusal_is_its_own_fixed_text_with_no_tool_and_no_model():
    chat, selector, gateway, explainer = chat_with([MURAT_ACCOUNTS])

    reply = chat.answer("Deniz'in bakiyesi ne kadar?", TOKEN, DAY)

    # Before AI-12: "Kişisel hesaplar: 644.659,42 TL", the user's own balance shown as Deniz's.
    assert (reply.type, reply.answer, reply.tool_calls, reply.cards) == ("outOfScope", OTHER_PERSON_TEXT, [], [])
    assert reply.suggestions == [BALANCE, SENT_LAST_MONTH, THIS_MONTH]
    assert selector.calls == [] and explainer.calls == [] and gateway.calls == []


def test_a_bare_name_after_a_balance_answer_is_no_follow_up():
    chat, selector, gateway, _ = chat_with([MURAT_ACCOUNTS])
    chat.answer("Bakiyem ne kadar?", TOKEN, DAY)

    reply = chat.answer("Peki Deniz'in?", TOKEN, DAY)

    # Read with the last call, it would show the user's own balance again.
    assert (reply.type, reply.answer) == ("outOfScope", OTHER_PERSON_TEXT)
    assert [c[0] for c in gateway.calls] == ["get_accounts"]


def test_the_text_says_why_and_the_way_that_is_open():
    assert OTHER_PERSON_TEXT == ("Yalnız sizin hesaplarınızın bilgisini gösterebilirim; başka bir kişinin ya da müşterinin bakiyesini, "
                                 "harcamasını veya gelirini göremem. Bir kişiyle aranızdaki para hareketlerini sorabilirsiniz, örneğin: "
                                 "“Geçen ay kime para gönderdim?”")


def test_the_measurement_counts_the_refusal_as_its_own_kind():
    chat, _, _, _ = chat_with([MURAT_ACCOUNTS])

    # K4-B: the tool-selection cases expect "other_person", not the general out-of-scope text.
    assert outcome_of(chat.answer("Deniz'in bakiyesi ne kadar?", TOKEN, DAY)).kind == "other_person"


def test_a_merchant_the_code_does_not_know_reads_as_a_person():
    # Known limit (AI-12): only Netflix and Spotify are merchants to the code (grounding._BRANDS); the customer's other merchants are
    # data the router does not see. "Starbucks'ın harcamaları" is refused with the reason, never answered with another figure.
    # "Starbucks harcamalarım" and "Starbucks'a harcadıklarım", the usual wordings, keep their way.
    assert Router(None).route("Starbucks'ın harcamaları ne kadar?", None, 30.0) == OTHER_PERSON
    assert Router(None).route("Starbucks harcamalarım", None, 30.0) == Route((GENERAL,))
    assert Router(None).route("Starbucks'a harcadıklarım", None, 30.0) == Route((GENERAL,))
