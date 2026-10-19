using BankingAI.Application.Analytics;

namespace BankingAI.McpServer.Tools;

/// <summary>
/// The periods a model may choose: every named period of the API except <see cref="PeriodKind.Custom"/>, because dates
/// are never the model's to choose (ANA-20). Names match <see cref="PeriodKind"/> exactly.
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
