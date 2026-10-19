using System.Text.Json;
using System.Text.Json.Serialization;

namespace BankingAI.Hosting.Json;

/// <summary>
/// The JSON rules every program applies to what it reads and writes, so the API and the MCP server produce the same
/// shapes for the same results (MCP-a, decision K9-A).
/// </summary>
public static class BankingJson
{
    /// <summary>
    /// Enums travel as their exact names ("Purchase"); numbers and other casings are refused as input. Money is a JSON
    /// number; quoted numbers are refused (API-15, decision K1-A).
    /// </summary>
    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new StrictEnumConverter());
        options.NumberHandling = JsonNumberHandling.Strict;
        return options;
    }

    /// <summary>
    /// The same output as <see cref="Configure"/> (enum names, money as numbers) for MCP tools. The only difference is
    /// the enum converter: the MCP SDK writes the list of names into a tool's schema only for the standard one, and
    /// that list is what the model chooses from. Exact-name input is enforced by the MCP server's argument filter
    /// against that very schema, before any binding.
    /// </summary>
    public static JsonSerializerOptions ConfigureForTools(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.NumberHandling = JsonNumberHandling.Strict;
        return options;
    }
}
