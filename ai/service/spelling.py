"""Reads a word one letter off a word the rules look for as that word (CHAT-13 PR2).

With a letter left out, typed twice, swapped or hit beside, the rules missed the word and the turn went to the wrong tool: on the
CHAT-13 PR1 sample the model gave another tool's figure for 11 of 116 typed questions (none of the 80 written ones). Decisions:
K2-B (a closed list: only the stems the rules look for, so a correction can only bring back a word a rule knew, never a new meaning),
K3-C (the whole turn reads the corrected message), K4-D (words of five letters or more, plus "maaş"), K11-C (month names as whole
words), K12-C (words of their own are kept: no "geçen" on the list, "gelin" and "azaldı" left alone by the edit rules below), K14-A
(names, merchants and numbers are never touched: they are not on the list). Standard library only, deterministic.
"""

import re

from answer_check import fold_rule, fold_turkish

# The stems the word rules of routing, accounts and grounding look for, in Turkish; a word that starts with one is left as it is.
_STEM_WORDS = (
    "harca", "harcı", "alışveriş", "masraf", "kategori", "işyeri", "karşılaştır", "abonelik", "yaklaşan", "düzenli",
    "bakiye", "hesap", "hesab", "gelir", "kazanc", "transfer", "havale", "gönder", "yolla",
    "bütçe", "tasarruf", "biriktir", "azalt",
    "market", "akaryakıt", "fatura", "eğlence", "giyim", "ulaşım", "restoran", "yemek",
    "nerede", "nerelerde", "nelerde",
)
# K11-C: the month names of five letters or more, compared whole (the letters before an apostrophe or an ending): "Eylüll'de",
# "Eyül'de". Four-letter months ("ekim", "mart", "ocak") are one letter from words of their own ("ekip", "mars").
_MONTH_WORDS = ("şubat", "nisan", "mayıs", "haziran", "temmuz", "ağustos", "eylül", "kasım", "aralık")
# The budget's verb, whole for the same reason: as a stem "ayırd" turned "ayında" (in the month) into "ayırda" (CHAT-13 PR2 model run:
# "1.500 euro ayıırdım, ne kadar kaldı?" got this month's total). "ayırım" (a word of its own) is kept.
_WHOLE_VERBS = ("ayırdım", "ayırdığım", "ayırsam", "ayıracağım")
# K4-D: "maaş" has four letters, below the length; its common typo is listed by hand ("maç" is a word of its own, never changed).
_SHORT_WORDS = (("maşım", "maaşım"), ("maşımı", "maaşımı"), ("maşımın", "maaşımın"), ("maşı", "maaşı"), ("maşlar", "maaşlar"),
                ("maşlarım", "maaşlarım"))
# The stems whose last letter may be mistyped too ("Bakiym", "haval yaptıklarım"). Not the others, where Turkish words differ from
# them only there: "gelin"/"gelir", "azaldı"/"azalt", "kazanır"/"kazanc", "giyin"/"giyim".
_LAST_LETTER_WORDS = ("bakiye", "market", "transfer", "havale", "gönder", "tasarruf", "biriktir", "fatura",
                      # "harcsdım", "bütçrmi": keys beside the last letter; the only word one letter from "harca" there is "harcı", a stem.
                      "harca", "bütçe")
# K12-C: words of their own that one of the edits would turn into a stem ("havalimanı" is no "havale", "nereye" no "nerede"). Found
# by test_no_correctly_written_question_is_changed; "öneri" left the list for the same reason ("önerdiğin" became "öneriğin").
_KEPT_BEGINNINGS = ("havalı", "havalim", "nereye", "ayırım")

STEMS = tuple(fold_rule(word) for word in _STEM_WORDS)
MONTHS = tuple(fold_rule(word) for word in _MONTH_WORDS)
WHOLE = MONTHS + tuple(fold_rule(word) for word in _WHOLE_VERBS)
_SHORT = {fold_rule(typed): fold_rule(meant) for typed, meant in _SHORT_WORDS}
_LAST_LETTER = frozenset(fold_rule(word) for word in _LAST_LETTER_WORDS)
_KEPT = tuple(fold_rule(word) for word in _KEPT_BEGINNINGS)
# The listed words as they are written, for a user who typed Turkish letters.
_TURKISH = {fold_rule(word): word for word in (*_STEM_WORDS, *_MONTH_WORDS, *_WHOLE_VERBS, *(meant for _, meant in _SHORT_WORDS))}
MIN_LENGTH = 5
# K5-A: the ending of a what-if ("alsaydım", "biriktirseydim") with the typos people make in it, for the rules to share: "alsadım",
# "alsaydm", "biriktirsydim", "alsaydmı" (the question glued on), "biriktirseydi" (s and the vowel kept: "neydi" is none) and the
# wish form "biriktireydim", "alaydım". Folded text; written in Turkish and folded once (fold_rule).
# A vowel or "y" must follow the "s": "harcsdım" (a key beside "a") is a spending question, not a what-if.
WHAT_IF_ENDING = fold_rule(r"(?:s(?:[ae]y?|y)d[iı]?(?:m|k)[iı]?|s[ae]ydi|\w[ae]ydi(?:m|k))\b")

_LETTERS = "abcdefghijklmnopqrstuvwxyz"
_WORD = re.compile(r"[^\W\d_]+")


def _edits(word: str, last_letter: bool) -> set[str]:
    """Every spelling one edit away (a letter left out, added, replaced, or two neighbours swapped), never at the first letter
    ("parça" is no "harca"), and at the last letter only when ``last_letter``."""
    end = len(word) if last_letter else len(word) - 1
    found = set()
    for i in range(1, end):
        found.add(word[:i] + word[i + 1:])
        found.update(word[:i] + c + word[i + 1:] for c in _LETTERS if c != word[i])
        if i + 1 < len(word) and (last_letter or i + 1 < end):
            found.add(word[:i] + word[i + 1] + word[i] + word[i + 2:])
    for i in range(1, end + 1):
        found.update(word[:i] + c + word[i:] for c in _LETTERS)
    found.discard(word)
    return found


def _table(words: tuple[str, ...], last_letter) -> dict[str, str | None]:
    """Each misspelling and the listed word it is one edit from; None when it is one edit from two of them (no guess)."""
    table: dict[str, str | None] = {}
    for word in words:
        for typed in _edits(word, last_letter(word)):
            table[typed] = word if table.get(typed, word) == word else None
    return table


_STEM_TYPOS = _table(STEMS, lambda stem: stem in _LAST_LETTER)
_WHOLE_TYPOS = _table(WHOLE, lambda word: True)
_LONGEST = max(len(typed) for typed in _STEM_TYPOS)


def correct(message: str) -> str:
    """The message with every word one letter off a listed word written as that word; everything else as the user wrote it."""
    return _WORD.sub(lambda match: _corrected(match.group(0)), message)


def _corrected(word: str) -> str:
    folded = fold_turkish(word)
    if folded in _SHORT:
        return _cased(_SHORT[folded], word)
    # Before the length: a five-letter month one letter short is four ("Eyül").
    if _WHOLE_TYPOS.get(folded) is not None and not folded.startswith(_KEPT):
        return _cased(_WHOLE_TYPOS[folded], word)
    if len(folded) < MIN_LENGTH:
        return word
    if folded.startswith(STEMS) or folded in WHOLE or folded.startswith(_KEPT):
        return word
    # The longest misspelt beginning wins ("harrca…" before "harr…"); the ending is kept as typed ("…malarım").
    for length in range(min(len(folded), _LONGEST), 2, -1):
        stem = _STEM_TYPOS.get(folded[:length], "")
        if stem:
            return _cased(stem, word[:length]) + word[length:]
        if stem is None:
            return word
    return word


def _cased(folded: str, typed: str) -> str:
    """The listed word in Turkish letters (plain when the user typed without them), in the case the user typed."""
    # Folded is plain already; typed with Turkish letters, the word comes back with them ("bütem" → "bütçem").
    turkish = _TURKISH.get(folded, folded) if any(letter in "çğıöşüÇĞİÖŞÜ" for letter in typed) else folded
    if typed.isupper():
        return turkish.replace("i", "İ").replace("ı", "I").upper()
    return turkish[0].upper() + turkish[1:] if typed[0].isupper() else turkish
