"""ACC-01b: the chat turns of the balance, the purchase search, income and transfers.

Decisions K2-C (the words choose the tool; when the model chooses nothing code reads the call), K3-A (the amount limit is code's),
K4-B (the person the model writes must be in the user's words), K5-B (the cards come from the tool's own rows), K6-C ("yattı mı"
without a period is this month), K8-A (two follow-ups under each answer).
"""

from datetime import date

from chat import ToolCall
from dates import CustomerDay
from prompts import OUT_OF_SCOPE_TEXT
from test_chat import DEFINITIONS, PERIOD, PERIOD_CHOICES, PERIOD_QUESTION, TOKEN, FakeGateway, service
from test_facts_accounts import HASAN_TO_SELIN, MURAT_ACCOUNTS, MURAT_OVER_2000, MURAT_SALARY, SEPTEMBER, THIS_MONTH, search

DAY = CustomerDay(date(2026, 10, 10), "Europe/Istanbul")
DATE = {"type": ["string", "null"]}
TEXT = {"type": ["string", "null"], "minLength": 1, "maxLength": 150}
AMOUNT = {"type": ["number", "null"]}
CURRENCY = {"type": ["string", "null"], "enum": ["TRY", "EUR", "USD", "GBP", None]}


def definition(name, properties, required=("period",)):
    return {"type": "function", "function": {"name": name, "description": "Açıklama.", "parameters": {
        "type": "object", "properties": properties, "required": list(required)}}}


# The four tools as ACC-01a publishes them (AccountTools).
ACCOUNT_DEFINITIONS = [
    definition("get_accounts", {}, ()),
    definition("search_transactions", {"period": PERIOD, "from": DATE, "to": DATE, "minAmount": AMOUNT, "maxAmount": AMOUNT,
                                       "currencyCode": CURRENCY, "merchant": TEXT, "categoryCode": {"type": ["string", "null"]}}),
    definition("get_income", {"period": PERIOD, "from": DATE, "to": DATE}),
    definition("get_transfers", {"period": PERIOD, "from": DATE, "to": DATE,
                                 "direction": {"type": ["string", "null"], "enum": ["Sent", "Received", None]}, "counterparty": TEXT}),
]


class AccountGateway(FakeGateway):
    def list_tools(self, token, deadline):
        super().list_tools(token, deadline)
        return DEFINITIONS + ACCOUNT_DEFINITIONS


def chat_with(results, turns=None, calls=()):
    return service(list(calls), gateway=AccountGateway(list(results)), turns=turns)


def shown(selector, turn=0):
    return [t["function"]["name"] for t in selector.calls[turn][1]]


def test_the_balance_needs_no_model():
    chat, selector, gateway, explainer = chat_with([MURAT_ACCOUNTS])

    reply = chat.answer("Bakiyem ne kadar?", TOKEN, DAY)

    # get_accounts takes no argument: the words chose it, so there is nothing for the model to fill in.
    assert selector.calls == [] and explainer.calls == []
    assert gateway.calls[0][:2] == ("get_accounts", {})
    assert (reply.type, reply.answer, reply.tool_calls) == ("answer", "Kişisel hesaplarınızda toplam 644.659,42 TL var.",
                                                            [ToolCall("get_accounts", {})])
    assert [c.title for c in reply.cards] == ["Kişisel hesaplar"]
    assert reply.suggestions == ["Bu ay ne kadar harcadım?", "Bu ay maaşım yattı mı?", "Geçen ay kime para gönderdim?"]


def test_did_my_salary_arrive_is_about_this_month():
    salary = search(THIS_MONTH, [], [])
    chat, selector, gateway, _ = chat_with([salary], turns=[[ToolCall("get_income", {"period": "ThisMonth"})]])

    reply = chat.answer("Maaşım yattı mı?", TOKEN, DAY)

    # K6-C: the model reads "bu ay" beside the question and is shown the income tool alone.
    assert (selector.calls[0][0], shown(selector)) == ("Maaşım yattı mı? (bu ay)", ["get_income"])
    assert gateway.calls[0][:2] == ("get_income", {"period": "ThisMonth"})
    assert (reply.type, reply.answer) == ("answer", "1-10 Ekim 2026: hesaplarınıza maaş ya da gelir yatmadı.")


def test_a_said_period_is_run_and_carded():
    chat, _, gateway, explainer = chat_with([MURAT_SALARY], turns=[[ToolCall("get_income", {"period": "LastMonth"})]])

    reply = chat.answer("Geçen ay maaşım ne kadar yattı?", TOKEN, DAY)

    assert gateway.calls[0][:2] == ("get_income", {"period": "LastMonth"})
    assert (reply.answer, explainer.calls) == ("Eylül 2026: 1 gelir, toplam 85.000,00 TL.", [])
    assert [c.title for c in reply.cards] == ["Gelir · Eylül 2026"]


def test_when_the_model_chooses_nothing_code_reads_the_call():
    # K2-C (the K13-A pattern): the 4b model sometimes returns no call for a list of one.
    chat, _, gateway, _ = chat_with([MURAT_SALARY], turns=[[]])

    chat.answer("Geçen ay maaşım ne kadar yattı?", TOKEN, DAY)

    assert gateway.calls[0][:2] == ("get_income", {"period": "LastMonth"})


def test_a_period_neither_said_nor_implied_is_asked():
    chat, _, gateway, _ = chat_with([], turns=[[ToolCall("get_transfers", {"period": "ThisMonth", "direction": "Sent"})]])

    reply = chat.answer("Kime para gönderdim?", TOKEN, DAY)

    assert (reply.type, reply.answer, reply.choices, gateway.calls) == ("clarification", PERIOD_QUESTION, PERIOD_CHOICES, [])


def test_the_direction_and_the_limit_are_code_s():
    chat, _, gateway, _ = chat_with(
        [search(SEPTEMBER, [], []), MURAT_OVER_2000],
        turns=[[ToolCall("get_transfers", {"period": "LastMonth", "direction": "Received"})],
               [ToolCall("search_transactions", {"period": "LastMonth", "maxAmount": 2000})]])

    chat.answer("Geçen ay kime para gönderdim?", TOKEN, DAY)
    reply = chat.answer("Geçen ay 2.000 TL üstü harcamalarım", TOKEN, DAY)

    assert gateway.calls[0][:2] == ("get_transfers", {"period": "LastMonth", "direction": "Sent"})
    assert gateway.calls[1][:2] == ("search_transactions", {"period": "LastMonth", "minAmount": 2000, "currencyCode": "TRY"})
    assert reply.answer == "Eylül 2026: 7 harcama, toplam 41.363,50 TL."


def test_a_person_the_user_did_not_name_is_left_out():
    chat, _, gateway, _ = chat_with([HASAN_TO_SELIN], turns=[
        [ToolCall("get_transfers", {"period": "Last3Months", "counterparty": "Annem"})]])

    chat.answer("Son 3 ayda kime para gönderdim?", TOKEN, DAY)

    assert gateway.calls[0][:2] == ("get_transfers", {"period": "Last3Months", "direction": "Sent"})


def test_a_follow_up_keeps_the_limit_of_the_search():
    chat, selector, gateway, _ = chat_with(
        [MURAT_OVER_2000, search(THIS_MONTH, [], [])],
        turns=[[ToolCall("search_transactions", {"period": "LastMonth"})], [ToolCall("search_transactions", {"period": "ThisMonth"})]])

    chat.answer("Geçen ay 2.000 TL üstü harcamalarım", TOKEN, DAY)
    chat.answer("Peki bu ay?", TOKEN, DAY)

    # The last call's group is shown again, and "peki" carries the user's own figure (CHAT-10 D12-A).
    assert shown(selector, 1) == ["get_accounts", "search_transactions", "get_income", "get_transfers"]
    assert gateway.calls[1][:2] == ("search_transactions", {"period": "ThisMonth", "minAmount": 2000, "currencyCode": "TRY"})


def test_a_request_to_send_money_is_still_out_of_scope():
    chat, selector, gateway, _ = chat_with([])

    reply = chat.answer("Selin'e 500 TL para gönder", TOKEN, DAY)

    assert (reply.type, reply.answer, selector.calls, gateway.calls) == ("outOfScope", OUT_OF_SCOPE_TEXT, [], [])


def test_a_short_question_without_peki_does_not_carry_the_limit():
    # CHAT-10 D12-A for the limit too: "Bu ay?" alone is read with the last call, but the user's own figure is not reused.
    chat, _, gateway, _ = chat_with(
        [MURAT_OVER_2000, search(THIS_MONTH, [], [])],
        turns=[[ToolCall("search_transactions", {"period": "LastMonth"})], [ToolCall("search_transactions", {"period": "ThisMonth"})]])

    chat.answer("Geçen ay 2.000 TL üstü harcamalarım", TOKEN, DAY)
    chat.answer("Bu ay?", TOKEN, DAY)

    assert gateway.calls[1][:2] == ("search_transactions", {"period": "ThisMonth"})
