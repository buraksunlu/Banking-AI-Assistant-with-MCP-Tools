from answer_eval import problems

CASE = {"type": "answer", "tool": "get_spending_summary", "facts": ["435,00 TL"], "answer": ["435"], "absent": ["Netflix"]}


def reply(**changes):
    base = {"type": "answer", "answer": "Bugün 435,00 TL harcadınız.", "toolCalls": [{"tool": "get_spending_summary", "arguments": {}}],
            "facts": [{"label": "Toplam harcama", "text": "435,00 TL"}]}
    return {**base, **changes}


def test_a_matching_reply_passes():
    assert problems(CASE, reply()) == []


def test_every_kind_of_difference_is_reported():
    wrong = reply(type="clarification", toolCalls=[], answer="Hangi dönem?",
                  facts=[{"label": "Netflix", "text": "229,99 TL"}])
    assert problems(CASE, wrong) == ["type clarification", "tool yok", "fact yok: 435,00 TL", "cevapta yok: 435", "olmamalı: Netflix"]


# UX-01 PR4: the cards are checked by their texts, as the phone shows them.

CARD = {"title": "Harcama · 10 Ekim 2026", "headline": {"amount": 435.0, "currency": "TRY", "text": "435,00 TL"}, "change": None,
        "simple": [{"kind": "stats", "rows": [], "points": [], "items": [{"label": "İşlem", "text": "3"}], "highlight": None}],
        "detailed": [], "sentence": "10 Ekim 2026: 3 harcama, toplam 435,00 TL."}


def test_a_card_case_passes_when_every_text_is_on_a_card():
    assert problems({**CASE, "card": ["435,00 TL", "İşlem"]}, reply(cards=[CARD])) == []


def test_a_missing_card_text_and_an_unwanted_card_are_reported():
    assert problems({**CASE, "card": ["999,00 TL"]}, reply(cards=[CARD])) == ["kartta yok: 999,00 TL"]
    assert problems({**CASE, "noCard": True}, reply(cards=[CARD])) == ["kart olmamalı"]
    assert problems({**CASE, "card": ["435,00 TL"]}, reply()) == ["kartta yok: 435,00 TL"]
