from decimal import Decimal

import pytest

from answer_check import AnswerCheck, check_answer, fold_turkish, parse_turkish_numbers
from contracts import Fact

QUESTION = "Bu ay ne kadar harcadım?"
FACTS = [
    Fact(label="Dönem", text="1-10 Ekim 2026"),
    Fact(label="Toplam harcama", text="735,00 TL"),
    Fact(label="Sınıflandırılmamış", text="145,00 TL"),
]


def check(answer: str, question: str = QUESTION, facts: list[Fact] = FACTS) -> AnswerCheck:
    return check_answer(question, facts, answer)


# Valid answers


@pytest.mark.parametrize(
    "answer",
    [
        "1-10 Ekim 2026 döneminde toplam 735,00 TL harcadınız.",
        "Bu ay 735 TL harcadınız.",  # same value as 735,00
        "Bu ay **735,00 TL** harcadınız; bunun 145,00 TL'si sınıflandırılmamış.",  # Markdown is allowed (AI-05 K4-B)
        "Bu bilgi elimdeki verilerde yok.",  # no numbers at all is fine
    ],
)
def test_answer_using_only_given_numbers_is_ok(answer):
    result = check(answer)
    assert result.ok, result
    assert result == AnswerCheck()


def test_a_number_from_the_question_is_allowed():
    facts = [Fact(label="Dönem", text="Nisan-Eylül 2026"), Fact(label="Toplam harcama", text="12.345,67 TL")]
    assert check("Son 6 ayda 12.345,67 TL harcadınız.", question="Son 6 ayda ne kadar harcadım?", facts=facts).ok


def test_a_number_from_a_label_is_allowed():
    facts = [Fact(label="Eylül 2026 harcaması", text="1.425,00 TL")]
    assert check("Eylül 2026'da 1.425,00 TL harcadınız.", facts=facts).ok


def test_the_sign_is_ignored():
    # "-415,00 TL" in the data and "415,00 TL azaldı" in the answer say the same thing.
    assert check("Harcamanız 415,00 TL azaldı.", facts=[Fact(label="Değişim", text="-415,00 TL")]).ok
    assert check("Değişim -415,00 TL.", facts=[Fact(label="Fark", text="415,00 TL")]).ok


# Invalid answers


def test_a_computed_number_is_reported_as_written():
    result = check("Günlük ortalama 73,50 TL harcıyorsunuz.")
    assert not result.ok
    assert result.invented_numbers == ("73,50",)


def test_small_counts_are_checked_too():
    # K3-A: a count like "3 kategori" is not in the data either; counting is where models slip.
    assert check("3 kategoride harcama yaptınız.").invented_numbers == ("3",)


def test_a_repeated_invented_number_is_listed_once():
    assert check("73,50 TL günlük, yani 73,50 TL.").invented_numbers == ("73,50",)


@pytest.mark.parametrize("answer", ["", "   \n"])
def test_empty_answer_is_rejected(answer):
    result = check(answer)
    assert not result.ok
    assert result.empty


@pytest.mark.parametrize(
    ("answer", "mention"),
    [
        ("Bu tutar backend tarafından hesaplanmıştır: 735,00 TL.", "backend"),
        ("Bu tutar BACKEND tarafından hesaplanmıştır.", "backend"),
        ("<veri> içinde 735,00 TL var.", "<veri"),
        ("Verideki </veri> etiketinden sonra 735,00 TL.", "</veri"),
    ],
)
def test_talking_about_the_system_is_rejected(answer, mention):
    result = check(answer)
    assert not result.ok
    assert mention in result.forbidden_mentions


# Logging


def test_description_has_counts_but_no_amounts():
    description = check("Günlük ortalama 73,50 TL, toplam 9,99 TL.").describe()
    assert description == "empty: no, invented numbers: 2, forbidden mentions: 0"
    assert "73,50" not in description


# Shared helpers (also used by ai/model_check/scoring.py)


def test_numbers_are_read_in_turkish_format():
    assert parse_turkish_numbers("Ekim 2026'da 1.840,00 TL harcadınız; bu %29,17 artış.") == [
        Decimal("2026"), Decimal("1840.00"), Decimal("29.17")]


def test_turkish_letters_fold_to_the_same_text():
    assert fold_turkish("TALİMAT") == fold_turkish("TALIMAT") == fold_turkish("talımat") == "talimat"
