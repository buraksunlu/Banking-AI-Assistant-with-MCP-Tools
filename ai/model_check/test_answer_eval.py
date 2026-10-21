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
