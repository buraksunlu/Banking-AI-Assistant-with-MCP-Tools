namespace BankingAI.McpServer.Tools;

/// <summary>
/// The category codes a model may choose (MCP-17, MCP-18), a closed set so a made-up code never reaches the backend. The
/// names are the codes of the category table exactly; a test compares the two, so the lists cannot drift apart.
/// </summary>
public enum ToolCategory
{
    FOOD,
    GROCERY,
    CLOTHING,
    TRANSPORTATION,
    ENTERTAINMENT,
    SUBSCRIPTION,
    TRAVEL,
    FUEL,
    BILLS,
    HEALTH,
    OTHER
}
