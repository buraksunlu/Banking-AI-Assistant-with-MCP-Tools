using BankingAI.Application.Analytics;

namespace BankingAI.McpServer.Tools;

/// <summary>
/// The comparisons a model may choose (MCP-09): the two named ones of the API, never <see cref="ComparisonKind.Custom"/>,
/// whose four dates would be the model's to choose. Names match <see cref="ComparisonKind"/> exactly.
/// </summary>
public enum ToolComparison
{
    PreviousFullMonth,
    MonthToDate
}
