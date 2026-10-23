using BankingAI.Application.Analytics;

namespace BankingAI.McpServer.Tools;

/// <summary>
/// Every named period of the API's analyses, plus <see cref="PeriodKind.Custom"/> for a range of days (CHAT-08). The model
/// never chooses dates (ANA-20): the AI service hides <c>Custom</c>, <c>from</c> and <c>to</c> from it and fills them from
/// dates its own code read from the user's words. <see cref="PeriodKind.AllTime"/> stays out, because only a transaction
/// list pages through it (API-17). Names match <see cref="PeriodKind"/> exactly.
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
    ThisYear,
    Custom
}
