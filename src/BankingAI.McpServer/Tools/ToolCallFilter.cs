using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace BankingAI.McpServer.Tools;

/// <summary>
/// Wraps every tool call. Before: enforces the tool's published input schema, because the SDK publishes the schema but
/// does not apply it, and its enum binding would quietly accept "yesterday" for "Yesterday"; model arguments are
/// untrusted, so what the model is shown is exactly what is accepted. After: turns the contract errors thrown by tool
/// bodies (<see cref="ToolErrors"/>) into the bare <c>{code, message}</c> result (K10-A). Left to the SDK, an exception
/// becomes "An error occurred invoking 'x': …", a sentence the AI service would have to parse. Backend input rules
/// (blank text, NUL) still run in the services.
/// </summary>
public static class ToolCallFilter
{
    /// <summary>The call-tool filter.</summary>
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Create(McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            if (context.MatchedPrimitive is McpServerTool tool
                && Problem(tool.ProtocolTool.InputSchema, context.Params?.Arguments) is { } problem)
                return ToolErrors.Result(ToolErrors.InvalidArguments, problem);

            try
            {
                return await next(context, cancellationToken);
            }
            // A protocol error (an unknown tool, for example) is not a tool result and passes through unchanged.
            catch (ModelContextProtocol.McpException error) when (error is not ModelContextProtocol.McpProtocolException)
            {
                return FromContract(error.Message);
            }
        };

    // ToolErrors writes the contract into the exception message. An McpException without it (one the SDK raised itself)
    // is a failure we did not map, so it is reported without detail.
    private static CallToolResult FromContract(string text)
    {
        try
        {
            using var json = JsonDocument.Parse(text);
            if (json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("code", out var code) && json.RootElement.TryGetProperty("message", out var message))
                return ToolErrors.Result(code.GetString()!, message.GetString()!);
        }
        catch (JsonException)
        {
        }
        return ToolErrors.Result(ToolErrors.InternalError, ToolErrors.InternalErrorMessage);
    }

    // The first broken rule, worded for the model and the log; null when the arguments follow the schema.
    private static string? Problem(JsonElement schema, IDictionary<string, JsonElement>? arguments)
    {
        arguments ??= new Dictionary<string, JsonElement>();
        var properties = schema.GetProperty("properties");
        foreach (var name in arguments.Keys)
            if (!properties.TryGetProperty(name, out _))
                return $"Tanınmayan argüman: {name}.";
        if (schema.TryGetProperty("required", out var required))
            foreach (var name in required.EnumerateArray().Select(r => r.GetString()!))
                if (!arguments.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null)
                    return $"Zorunlu argüman eksik: {name}.";

        foreach (var (name, value) in arguments)
        {
            var rule = properties.GetProperty(name);
            if (rule.TryGetProperty("enum", out var allowed))
            {
                if (value.ValueKind != JsonValueKind.String || !allowed.EnumerateArray().Any(a => a.GetString() == value.GetString()))
                    return $"{name} şunlardan biri olmalı (tam adıyla): {string.Join(", ", allowed.EnumerateArray().Select(a => a.GetString()))}.";
                continue;
            }
            if (rule.TryGetProperty("type", out var count) && count.ValueKind == JsonValueKind.String && count.GetString() == "integer")
            {
                // The SDK's binding would turn "3" and 3.5 into 3: only a whole JSON number is a count the model meant.
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
                    return $"{name} tam sayı olmalı.";
                if (rule.TryGetProperty("minimum", out var minimum) && number < minimum.GetInt32()
                    || rule.TryGetProperty("maximum", out var maximum) && number > maximum.GetInt32())
                    return $"{name} şema sınırları dışında.";
                continue;
            }
            if (rule.TryGetProperty("type", out var type) && type.GetString() == "string")
            {
                if (value.ValueKind != JsonValueKind.String)
                    return $"{name} metin olmalı.";
                // Counted in Unicode characters, like the database rule (InputRules.Text).
                var length = value.GetString()!.EnumerateRunes().Count();
                if (rule.TryGetProperty("minLength", out var min) && length < min.GetInt32()
                    || rule.TryGetProperty("maxLength", out var max) && length > max.GetInt32())
                    return $"{name} uzunluğu şema sınırları dışında.";
            }
        }
        return null;
    }
}
