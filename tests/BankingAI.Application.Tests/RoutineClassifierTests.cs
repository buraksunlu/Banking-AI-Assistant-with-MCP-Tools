using BankingAI.Application.Analytics;
using Xunit;

namespace BankingAI.Application.Tests;

// DEMO-02 K2-A: a purchase is routine when its statement carries an installment number or the recurring rule holds for its merchant;
// otherwise an essential category is "Temel". Everything else is non-routine.
public sealed class RoutineClassifierTests
{
    [Theory]
    [InlineData("APPLE STORE TAKSIT 03/12", 3, 12)]
    [InlineData("IKEA TAKSIT 01/09", 1, 9)]
    public void An_installment_number_is_read_from_the_statement(string raw, int number, int count) =>
        Assert.Equal((number, count), RoutineClassifier.Installment(raw));

    [Theory]
    [InlineData("OKUL TAKSIT ODEMESI")]
    [InlineData("TAKSIT 3/12")]
    [InlineData("SINEMA BILET")]
    public void Other_statements_have_no_installment(string raw) => Assert.Null(RoutineClassifier.Installment(raw));

    [Theory]
    [InlineData("MEDIAMARKT TAKSIT 02/06", "OTHER", false, RoutineReason.Installment)]
    [InlineData("Netflix aylık abonelik ödemesi", "SUBSCRIPTION", true, RoutineReason.Subscription)]
    [InlineData("SU FATURA TAHSILATI", "BILLS", true, RoutineReason.Bill)]
    [InlineData("MACFIT UYELIK", "ENTERTAINMENT", true, RoutineReason.Recurring)]
    [InlineData("POS MIGROS JET ATASEHIR", "GROCERY", false, RoutineReason.Essential)]
    [InlineData("EV INTERNETI FATURA", "BILLS", false, RoutineReason.Essential)]
    public void Routine_reasons(string raw, string category, bool recurring, RoutineReason expected) =>
        Assert.Equal(expected, RoutineClassifier.Classify(raw, category, recurring));

    [Theory]
    [InlineData("SINEMA BILET", "ENTERTAINMENT")]
    [InlineData("POS 3301 CAN KIRTASIYE MODA", "OTHER")]
    [InlineData("POS 1234 ODEME", null)]
    public void Everything_else_is_non_routine(string raw, string? category) =>
        Assert.Null(RoutineClassifier.Classify(raw, category, recurring: false));
}
