using BankingAI.Application.Analytics;

namespace BankingAI.McpServer.Tools;

/// <summary>
/// The periods a model may choose: every named period of the API's analyses, so neither <see cref="PeriodKind.Custom"/>,
/// because dates are never the model's to choose (ANA-20), nor <see cref="PeriodKind.AllTime"/>, which only a transaction
/// list pages through (API-17). Names match <see cref="PeriodKind"/> exactly.
/// </summary>
public enum ToolPeriod
{
    Today,
    Yesterday,
    ThisWeek,
    LastWeek,
    Last7Days,
    Last30Days,
    ThisMonth,
    LastMonth,
    Last3Months,
    Last6Months,
    ThisYear
}
