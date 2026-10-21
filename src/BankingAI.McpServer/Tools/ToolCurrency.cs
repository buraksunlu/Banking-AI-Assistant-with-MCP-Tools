namespace BankingAI.McpServer.Tools;

/// <summary>
/// The currencies of a budget (MCP-17): the ones the database accepts. A budget is never compared across currencies,
/// so the model must name one, and the AI service checks that the user did (decision K2-A).
/// </summary>
public enum ToolCurrency
{
    TRY,
    USD,
    EUR,
    GBP
}
