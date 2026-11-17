"""CHAT-13 PR2: one letter off a word the rules look for is read as that word (K2-B, closed list; K4-D, five letters or more and
"maaş"; K11-C, month names; K12-C, words of their own are kept). Names, numbers and words off the list are never touched (K14-A)."""

import json
from pathlib import Path

import pytest

from spelling import correct

CHECK = Path(__file__).resolve().parent.parent / "model_check"


@pytest.mark.parametrize("typed, read", [
    ("Geçen ay 2.000 TL üstü hacamalarım neler?", "Geçen ay 2.000 TL üstü harcamalarım neler?"),   # a letter left out
    ("Son 6 ayda aylık harrcamam", "Son 6 ayda aylık harcamam"),                                 # a letter twice
    ("Starbucks hracamamı %20 azaltsam ne kazanırım?", "Starbucks harcamamı %20 azaltsam ne kazanırım?"),   # two swapped
    ("Maaşımın ne kadarını hatcadım?", "Maaşımın ne kadarını harcadım?"),                         # a key beside it
    ("Maret harcamamı %20 azaltsam", "Market harcamamı %20 azaltsam"),
    ("Bakiym ne kadar?", "Bakiye ne kadar?"),
    ("Heaplarımı göster", "Hesaplarımı göster"),
    ("Son 3 ayda kzaancım ne kadar?", "Son 3 ayda kazancım ne kadar?"),
    ("Geçen ay eğlnece harcamamı dolar alsaydım", "Geçen ay eğlence harcamamı dolar alsaydım"),
    ("Geçen ay kime para göndrdim?", "Geçen ay kime para gönderdim?"),
    ("Bu ay 1.000 TL bütem var", "Bu ay 1.000 TL bütçem var"),
    ("geçen ay hacamalarim", "geçen ay harcamalarim"),                                           # typed without Turkish letters
    ("Maşım yattı mı?", "Maaşım yattı mı?"),                                                     # K4-D: the one short word
    ("Eylüll'de transferlerim neler?", "Eylül'de transferlerim neler?"),                         # K11-C: a month as a whole word
    ("Eyül'de 500 liranın altındaki harcamalarım", "Eylül'de 500 liranın altındaki harcamalarım"),
    ("HACAMALARIM", "HARCAMALARIM"),
    ("Bu ay harcamalara 1.500 euro ayıırdım, ne kadar kaldı?", "Bu ay harcamalara 1.500 euro ayırdım, ne kadar kaldı?"),
    ("Geçen ay ne kadar harcsdım?", "Geçen ay ne kadar harcadım?"),                                # the key beside the last letter
    ("Bütçrmi kısıp tasarruf etsem?", "Bütçemi kısıp tasarruf etsem?"),
])
def test_one_letter_off_a_listed_word_is_read_as_it(typed, read):
    assert correct(typed) == read


@pytest.mark.parametrize("text", [
    # K12-C: words of their own, one letter off a listed one.
    "Dün gece ne harcadım?", "Maç biletine ne harcadım?", "Masa aldım", "Gelin için ne harcadım?", "Para geliyor mu?",
    "Harcamam azaldı mı?", "Parçalar için ne ödedim?", "Hasan'dan gelince bakarım", "Ekip yemeği ne tuttu?",
    # K14-A: names and merchants are the user's, numbers are never words.
    "Migrs'a ne kadar?", "Seiln'e ne kadar gönderdim?", "Hasa'dan para geldi mi?", "Bu ay 1.000 TL bütçem var",
    # Too many letters off, or one letter from two listed words ("neerde": "nerede" or "nelerde"): no guess.
    "Hrcmlrm neler?", "Neerde tasarruf edebilirim?", "Bu ay ne harcaıyorum",
    # Below five letters: "yola" is one letter from "yolla" and a word of its own.
    "Yola ne kadar harcadım?",
    # "ayında" is two letters from "ayırdım", "ayırım" a word of its own.
    "Eylül ayında ne harcadım?", "Ayırım yapmadan harcamalarım",
])
def test_these_are_left_as_they_are(text):
    assert correct(text) == text


def _clean_texts():
    templates = json.loads((CHECK / "phrasing_templates.json").read_text(encoding="utf-8"))
    yield from (item["text"] for item in templates.get("real", []))
    yield from (turn for item in templates.get("dialogs", []) for turn in item["turns"])
    for intent in templates["intents"]:
        for template in intent["templates"]:
            slots = {**intent.get("slots", {}), **template.get("slots", {})}
            yield template["text"].format(**{name: values[0]["text"] for name, values in slots.items()})
    yield from (case["text"] for case in json.loads((CHECK / "free_phrasings.json").read_text(encoding="utf-8"))["cases"])
    yield from (case["message"] for case in json.loads((CHECK / "tool_selection_cases.json").read_text(encoding="utf-8"))["cases"])


def test_no_correctly_written_question_is_changed():
    # The safety net of K12-C: every measured question as it is written today goes through untouched.
    changed = {text: correct(text) for text in _clean_texts() if correct(text) != text}
    assert changed == {}


# --- K5-A: the ending of a what-if, with its typos ---------------------------------------------------------------------------

from routing import WEALTH, Router  # noqa: E402
from grounding import mentions_period  # noqa: E402


@pytest.mark.parametrize("message", ["Dolar alsaydm ne olurdu?", "Geçen ay eğlence harcamamı altın alsadım?",
                                     "Ayda 1.000 TL biriktirsydim ne olurdu?", "Son 3 ayda dolar alsaydmı ne olurdu?",
                                     "Geçen ayki harcamalarımı biriktirseydi ne olurdu?", "Geçen ayki harcamalarımı biriktireydim nolurdu",
                                     "Aylık 1.000 TL 6 ay altın alaydım ne olurdu?"])
def test_a_mistyped_what_if_ending_is_still_a_what_if(message):
    assert Router(None).route(message, None, 30.0).groups == (WEALTH,)


@pytest.mark.parametrize("message", ["Geçen ay en büyük harcamam neydi?", "Bu ay ne kadar harcadım?", "Geçen ay param nereye gitti?",
                                     "Geçen ay ne kadar harcsdım?", "Bu ay ne kadar harcsdim"])
def test_other_endings_are_no_what_if(message):
    assert Router(None).route(message, None, 30.0).groups != (WEALTH,)


# --- K11-B: a month name counts as said only as it is written ---------------------------------------------------------------

@pytest.mark.parametrize("message", ["Eylül'de ne harcadım?", "eylülde", "Eylül ayında", "Eylül'den beri", "eylülün ilk haftası",
                                     "Ekim'de", "ekimdeki harcamalar", "Ağustos'ta", "martta", "Mart'ı", "ocağın", "Ocak'ta",
                                     "aralığın sonu", "Kasım'da", "Haziranda", "eylul'de", "EYLÜL"])
def test_a_written_month_is_a_period(message):
    assert mentions_period(message)


@pytest.mark.parametrize("message", ["Eylüüül'de transferlerim neler?", "Eylüll'de transferlerim", "marketten ne aldım"])
def test_a_month_written_otherwise_is_none(message):
    # "Eylüüül" is two letters off, so no correction reaches it; before K11-B the "eylül" in front made it this month's question.
    assert not mentions_period(message)
