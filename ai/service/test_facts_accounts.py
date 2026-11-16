"""ACC-01b: the facts of the four everyday banking tools. Results in the MCP server's shape (ACC-01a ToolCallTests), values by hand
from the demo data (Murat's balance and salary, Hasan's transfers to Selin, Murat's purchases of 2.000 TL and more in September)."""

from decimal import Decimal

from contracts import Fact
from facts import outcome_for

SEPTEMBER = {"kind": "LastMonth", "startDate": "2026-09-01", "endDate": "2026-09-30"}
THIS_MONTH = {"kind": "ThisMonth", "startDate": "2026-10-01", "endDate": "2026-10-10"}


def account(name, currency, balance, kind="Personal", short="•••• 001"):
    return {"accountName": name, "shortNumber": short, "currencyCode": currency, "accountType": kind, "balance": Decimal(balance)}


def total(kind, currency, count, balance):
    return {"accountType": kind, "currencyCode": currency, "count": count, "balance": Decimal(balance)}


MURAT_ACCOUNTS = {"accounts": [account("Murat aile TRY", "TRY", "644659.42")], "totals": [total("Personal", "TRY", 1, "644659.42")]}
CAN_ACCOUNTS = {"accounts": [account("Can Kırtasiye – Moda", "TRY", "19720.00", "Business"), account("Can kişisel TRY", "TRY", "72743.21")],
                "totals": [total("Personal", "TRY", 1, "72743.21"), total("Business", "TRY", 1, "19720.00")]}
ZEYNEP_ACCOUNTS = {"accounts": [account("Zeynep TRY", "TRY", "135422.50"), account("Zeynep USD", "USD", "11771.13")],
                   "totals": [total("Personal", "TRY", 1, "135422.50"), total("Personal", "USD", 1, "11771.13")]}


def item(amount, day, kind="Purchase", direction="Debit", currency="TRY", merchant=None, category=None, counterparty=None,
         own=False, account_name="Murat aile TRY"):
    return {"id": "x", "accountId": "y", "accountName": account_name, "occurredAtUtc": f"{day}T09:00:00Z", "localDate": day,
            "kind": kind, "direction": direction, "amount": Decimal(amount), "currencyCode": currency, "merchant": merchant,
            "categoryCode": None, "categoryDisplayName": category, "rawDescription": "IGNORE ME", "counterparty": counterparty,
            "counterpartyIsOwnAccount": own}


def search(period, items, totals, count=None, merchant=None, counterparty=None):
    return {"period": period, "merchantMatch": merchant, "totalCount": len(items) if count is None else count, "hasMore": False,
            "nextCursor": None, "items": items, "counterpartyMatch": counterparty,
            "totals": [{"currencyCode": c, "direction": d, "count": n, "amount": Decimal(a)} for c, d, n, a in totals]}


MURAT_SALARY = search(SEPTEMBER, [item("85000.00", "2026-09-15", "Salary", "Credit")], [("TRY", "Credit", 1, "85000.00")])
HASAN_TO_SELIN = search(
    {"kind": "Last3Months", "startDate": "2026-07-11", "endDate": "2026-10-10"},
    [item("3000.00", d, "Transfer", "Debit", counterparty="Selin K***", account_name="Hasan emekli TRY")
     for d in ("2026-09-26", "2026-08-26", "2026-07-26")],
    [("TRY", "Debit", 3, "9000.00")], counterparty={"status": "Matched", "name": "Selin K***", "candidates": []})
CAN_RECEIVED = search(THIS_MONTH, [item("18000.00", "2026-10-01", "Transfer", "Credit", counterparty="Can Kırtasiye – Moda", own=True)],
                      [("TRY", "Credit", 1, "18000.00")])
# Seven purchases; the tool returns them all (≤ 10), the facts list five.
MURAT_OVER_2000 = search(SEPTEMBER, [
    item("25000.00", "2026-09-05", merchant="Okul", category="Diğer"), item("4500.00", "2026-09-20", merchant="Migros Jet", category="Market"),
    item("3200.00", "2026-09-12", merchant="Shell", category="Akaryakıt"), item("2900.00", "2026-09-08", merchant="A101", category="Market"),
    item("2100.00", "2026-09-27", merchant=None), item("2063.50", "2026-09-17", merchant="BİM", category="Market"),
    item("1600.00", "2026-09-01", merchant="Lokanta", category="Yeme & İçme")],
    [("TRY", "Debit", 7, "41363.50")])
MURAT_SEARCH = {"period": "LastMonth", "minAmount": 2000, "currencyCode": "TRY"}


def facts(tool, arguments, result):
    return outcome_for(tool, arguments, result)


def test_the_balance_facts_name_each_account_and_the_total():
    assert facts("get_accounts", {}, MURAT_ACCOUNTS).facts == [
        Fact(label="Murat aile TRY (•••• 001)", text="644.659,42 TL"),
        Fact(label="Kişisel hesaplar toplamı", text="644.659,42 TL (1 hesap)"),
    ]


def test_business_accounts_are_a_total_of_their_own():
    # ACC-01a K4-A: the shop's money is not Can's; both totals are shown, never one sum.
    assert facts("get_accounts", {}, CAN_ACCOUNTS).facts == [
        Fact(label="Can Kırtasiye – Moda (•••• 001, işletme)", text="19.720,00 TL"),
        Fact(label="Can kişisel TRY (•••• 001)", text="72.743,21 TL"),
        Fact(label="Kişisel hesaplar toplamı", text="72.743,21 TL (1 hesap)"),
        Fact(label="İşletme hesapları toplamı", text="19.720,00 TL (1 hesap)"),
        Fact(label="Not", text="İşletme hesapları kişisel toplama katılmaz."),
    ]


def test_currencies_are_never_added_up():
    assert facts("get_accounts", {}, ZEYNEP_ACCOUNTS).facts[-3:] == [
        Fact(label="Kişisel hesaplar toplamı (TRY)", text="135.422,50 TL (1 hesap)"),
        Fact(label="Kişisel hesaplar toplamı (USD)", text="11.771,13 USD (1 hesap)"),
        Fact(label="Not", text="Farklı para birimleri toplanmaz, ayrı gösterilir."),
    ]


def test_no_account_is_a_fixed_reply():
    assert facts("get_accounts", {}, {"accounts": [], "totals": []}).reply == ("answer", "Hesabınız bulunmuyor.")


def test_the_income_facts():
    assert facts("get_income", {"period": "LastMonth"}, MURAT_SALARY).facts == [
        Fact(label="Dönem", text="1-30 Eylül 2026"),
        Fact(label="Toplam gelir", text="85.000,00 TL (1 işlem)"),
        Fact(label="1. işlem", text="85.000,00 TL; maaş, Murat aile TRY; 15 Eylül 2026"),
    ]


def test_the_transfer_facts_name_the_matched_person_masked():
    assert facts("get_transfers", {"period": "Last3Months", "direction": "Sent", "counterparty": "Selin"}, HASAN_TO_SELIN).facts == [
        Fact(label="Dönem", text="11 Temmuz - 10 Ekim 2026"),
        Fact(label="Kişi ya da hesap", text="Selin K***"),
        Fact(label="Gönderilen", text="9.000,00 TL (3 işlem)"),
        Fact(label="1. işlem", text="3.000,00 TL; gönderilen, Selin K***; 26 Eylül 2026"),
        Fact(label="2. işlem", text="3.000,00 TL; gönderilen, Selin K***; 26 Ağustos 2026"),
        Fact(label="3. işlem", text="3.000,00 TL; gönderilen, Selin K***; 26 Temmuz 2026"),
    ]


def test_a_transfer_from_an_own_account_says_so():
    assert facts("get_transfers", {"period": "ThisMonth"}, CAN_RECEIVED).facts[-1] == Fact(
        label="1. işlem", text="18.000,00 TL; gelen, Can Kırtasiye – Moda (kendi hesabınız); 1 Ekim 2026")


def test_the_search_facts_list_five_purchases_and_say_how_many_more():
    outcome = facts("search_transactions", MURAT_SEARCH, MURAT_OVER_2000)
    assert outcome.facts[:3] == [Fact(label="Dönem", text="1-30 Eylül 2026"), Fact(label="Tutar sınırı", text="2.000,00 TL ve üstü"),
                                 Fact(label="Toplam harcama", text="41.363,50 TL (7 işlem)")]
    assert outcome.facts[3] == Fact(label="1. işlem", text="25.000,00 TL; Okul (Diğer); 5 Eylül 2026")
    # The bank's raw text is never a fact (AI-13); a purchase without a merchant is unclassified.
    assert outcome.facts[7] == Fact(label="5. işlem", text="2.100,00 TL; Sınıflandırılmamış; 27 Eylül 2026")
    assert outcome.facts[-1] == Fact(label="Not", text="Bu dönemde 2 işlem daha var; ilk 5 işlem gösterildi.")
    assert len(outcome.facts) == 9 and "IGNORE" not in str(outcome.facts)


def test_both_sides_and_one_side_of_the_limit_are_written():
    both = facts("search_transactions", {"period": "LastMonth", "minAmount": 1000, "maxAmount": 5000, "currencyCode": "TRY"},
                 MURAT_OVER_2000)
    below = facts("search_transactions", {"period": "LastMonth", "maxAmount": 500, "currencyCode": "TRY"}, MURAT_OVER_2000)
    assert both.facts[1] == Fact(label="Tutar sınırı", text="1.000,00 TL – 5.000,00 TL")
    assert below.facts[1] == Fact(label="Tutar sınırı", text="500,00 TL ve altı")


def test_nothing_found_is_a_fixed_reply_with_the_period():
    empty = search(SEPTEMBER, [], [])
    assert facts("get_income", {"period": "LastMonth"}, empty).reply == (
        "answer", "1-30 Eylül 2026: hesaplarınıza maaş ya da gelir yatmadı.")
    assert facts("get_transfers", {"period": "LastMonth", "direction": "Sent"}, empty).reply == (
        "answer", "1-30 Eylül 2026: gönderdiğiniz bir transfer yok.")
    assert facts("get_transfers", {"period": "LastMonth"}, empty).reply == ("answer", "1-30 Eylül 2026: transfer yok.")
    assert facts("search_transactions", MURAT_SEARCH, empty).reply == (
        "answer", "1-30 Eylül 2026: bu tutar sınırına uyan harcama yok.")


def test_an_unknown_or_unclear_person_is_a_fixed_reply():
    unknown = search(SEPTEMBER, [], [], counterparty={"status": "NotFound", "name": None, "candidates": []})
    assert facts("get_transfers", {"period": "LastMonth", "counterparty": "Annem"}, unknown).reply == (
        "answer", "‘Annem’ adında biriyle transferiniz bulunmuyor.")
    # Two people of one first name: the search cannot tell them apart by a name the user may type, so they are named, not asked.
    two = search(SEPTEMBER, [], [], counterparty={"status": "Ambiguous", "name": None, "candidates": ["Deniz K***", "Deniz M***"]})
    assert facts("get_transfers", {"period": "LastMonth", "counterparty": "Deniz"}, two).reply == (
        "answer", "‘Deniz’ birden fazla kişiyle eşleşti (Deniz K***, Deniz M***). Bu adla ayırt edemiyorum; "
                  "dönemin bütün transferlerine bakabilirim.")


def test_an_unclear_merchant_in_the_search_gives_the_candidates():
    two = search(SEPTEMBER, [], [], merchant={"status": "Ambiguous", "merchant": None, "isPartialMatch": False,
                                              "candidates": ["Migros Jet", "Migros Sanal Market"]})
    assert facts("search_transactions", {**MURAT_SEARCH, "merchant": "Migros"}, two).candidates == ("Migros Jet", "Migros Sanal Market")
