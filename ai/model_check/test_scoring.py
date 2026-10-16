import unittest
from decimal import Decimal

from scoring import parse_turkish_numbers, score_explanation, score_structured, score_tool_call


def call(name: str, arguments) -> dict:
    return {"function": {"name": name, "arguments": arguments}}


class ParseTurkishNumbersTests(unittest.TestCase):
    def test_reads_thousands_decimals_percent_and_years(self):
        self.assertEqual(
            parse_turkish_numbers("Ekim 2026'da 1.840,00 TL harcadın; bu %29,17 artış."),
            [Decimal("2026"), Decimal("1840.00"), Decimal("29.17")])

    def test_english_decimal_is_read_as_two_numbers(self):
        # "29.17" is not a Turkish number; reading it as 29 and 17 makes the wrong format visible.
        self.assertEqual(parse_turkish_numbers("29.17"), [Decimal("29"), Decimal("17")])

    def test_text_without_numbers_gives_empty_list(self):
        self.assertEqual(parse_turkish_numbers("Sayı yok."), [])


class ScoreToolCallTests(unittest.TestCase):
    expected = {"tool": "get_merchant_spending", "args": {"merchant": "Migros", "period": "LastMonth"}}

    def test_exact_call_passes(self):
        score = score_tool_call(self.expected, [call("get_merchant_spending", {"merchant": "Migros", "period": "LastMonth"})])
        self.assertTrue(score.passed, score.detail)

    def test_arguments_given_as_json_text_are_read(self):
        score = score_tool_call(self.expected, [call("get_merchant_spending", '{"merchant": "Migros", "period": "LastMonth"}')])
        self.assertTrue(score.passed, score.detail)

    def test_wrong_tool_fails(self):
        score = score_tool_call(self.expected, [call("get_spending_summary", {"period": "LastMonth"})])
        self.assertFalse(score.passed)
        self.assertIn("get_spending_summary", score.detail)

    def test_wrong_value_fails(self):
        score = score_tool_call(self.expected, [call("get_merchant_spending", {"merchant": "Migros", "period": "ThisMonth"})])
        self.assertFalse(score.passed)
        self.assertIn("period", score.detail)

    def test_missing_argument_fails(self):
        score = score_tool_call(self.expected, [call("get_merchant_spending", {"merchant": "Migros"})])
        self.assertFalse(score.passed)
        self.assertIn("period", score.detail)

    def test_invented_extra_argument_fails(self):
        score = score_tool_call(self.expected, [call("get_merchant_spending",
                                                     {"merchant": "Migros", "period": "LastMonth", "currencyCode": "TRY"})])
        self.assertFalse(score.passed)
        self.assertIn("currencyCode", score.detail)

    def test_two_calls_fail(self):
        same = call("get_merchant_spending", {"merchant": "Migros", "period": "LastMonth"})
        score = score_tool_call(self.expected, [same, same])
        self.assertFalse(score.passed)

    def test_no_call_fails_when_one_is_expected(self):
        self.assertFalse(score_tool_call(self.expected, None).passed)

    def test_whole_number_matches_its_decimal_form(self):
        expected = {"tool": "check_budget", "args": {"amount": 5000, "currencyCode": "TRY"}}
        score = score_tool_call(expected, [call("check_budget", {"amount": 5000.0, "currencyCode": "TRY"})])
        self.assertTrue(score.passed, score.detail)

    def test_out_of_scope_question_passes_only_without_call(self):
        expected = {"tool": None}
        self.assertTrue(score_tool_call(expected, []).passed)
        self.assertTrue(score_tool_call(expected, None).passed)
        refused = score_tool_call(expected, [call("get_spending_summary", {"period": "ThisMonth"})])
        self.assertFalse(refused.passed)
        self.assertIn("get_spending_summary", refused.detail)


class ScoreStructuredTests(unittest.TestCase):
    expected = {"intent": "merchant_spending", "period": "LastMonth"}

    def test_matching_object_passes(self):
        score = score_structured(self.expected, '{"intent": "merchant_spending", "period": "LastMonth"}')
        self.assertTrue(score.passed, score.detail)

    def test_invalid_json_fails(self):
        score = score_structured(self.expected, "intent: merchant_spending")
        self.assertFalse(score.passed)
        self.assertIn("JSON", score.detail)

    def test_json_that_is_not_an_object_fails(self):
        self.assertFalse(score_structured(self.expected, '["merchant_spending"]').passed)

    def test_wrong_value_fails(self):
        score = score_structured(self.expected, '{"intent": "spending_summary", "period": "LastMonth"}')
        self.assertFalse(score.passed)
        self.assertIn("intent", score.detail)


class ScoreExplanationTests(unittest.TestCase):
    source = ["Dönem: Ekim 2026. Toplam harcama: 735,00 TL."]
    must_include = ["735,00 TL"]

    def test_text_using_only_given_numbers_passes(self):
        score = score_explanation(self.source, self.must_include, "Ekim 2026'da toplam 735,00 TL harcadın.")
        self.assertTrue(score.passed, score.detail)

    def test_invented_number_fails_and_is_named(self):
        score = score_explanation(self.source, self.must_include,
                                  "Ekim 2026'da 735,00 TL harcadın, geçen aydan 120 TL fazla.")
        self.assertFalse(score.passed)
        self.assertIn("120", score.detail)

    def test_missing_required_text_fails(self):
        # Same value, different text: AI-09 asks the model to quote the backend's text, not to rewrite it.
        score = score_explanation(self.source, self.must_include, "Ekim 2026'da 735 TL harcadın.")
        self.assertFalse(score.passed)
        self.assertIn("735,00 TL", score.detail)

    def test_empty_answer_fails(self):
        self.assertFalse(score_explanation(self.source, self.must_include, "   ").passed)


if __name__ == "__main__":
    unittest.main()
