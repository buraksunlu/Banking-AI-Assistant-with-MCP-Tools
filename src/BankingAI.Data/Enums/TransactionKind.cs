namespace BankingAI.Data.Enums;

// Hareketin bu hesap açısından ekonomik anlamı; Direction yalnızca paranın yönüdür.
public enum TransactionKind : short
{
    Purchase = 1,
    Sale = 2,
    Transfer = 3,
    Salary = 4,
    OpeningFunding = 5
}
