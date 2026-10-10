namespace BankingAI.Application.Analytics;

// LLM/tool argümanı olarak gelen sembolik dönem; tarih aralığını yalnızca backend hesaplar.
public enum PeriodKind
{
    ThisMonth = 1,
    LastMonth = 2,
    Last3Months = 3,
    Last6Months = 4,
    Custom = 5
}
