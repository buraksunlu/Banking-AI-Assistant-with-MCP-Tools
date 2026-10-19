namespace BankingAI.Application.Analytics;

// LLM/tool argümanı olarak gelen sembolik dönem; tarih aralığını yalnızca backend hesaplar.
public enum PeriodKind
{
    ThisMonth = 1,
    LastMonth = 2,
    Last3Months = 3,
    Last6Months = 4,
    Custom = 5,
    // ANA-20: day and week periods, so a model never has to work out "yesterday" or "this week" as dates.
    Today = 6,
    Yesterday = 7,
    ThisWeek = 8,
    LastWeek = 9,
    Last7Days = 10,
    Last30Days = 11,
    ThisYear = 12
}
