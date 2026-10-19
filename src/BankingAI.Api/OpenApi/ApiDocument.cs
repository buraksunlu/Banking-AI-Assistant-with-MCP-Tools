using System.Text.Json.Nodes;
using BankingAI.Hosting.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace BankingAI.Api.OpenApi;

/// <summary>
/// Makes the generated OpenAPI document say what the API really does (API-15): enums by their exact names, money as
/// JSON numbers, the bearer token on protected operations, and every error status with the shared problem body. The
/// generator alone describes enums as integers, money as "number or string" in double format and only a 200 answer.
/// </summary>
public static class ApiDocument
{
    /// <summary>The security scheme name used by every protected operation.</summary>
    public const string BearerScheme = "Bearer";

    /// <summary>The component name of the shared error body (ProblemDetails with <c>code</c> and <c>traceId</c>).</summary>
    public const string ProblemSchema = "Problem";

    private const string ProblemJson = "application/problem+json";
    private static readonly string[] IdentifierParameters = ["id", "accountId"];

    /// <summary>Registers the document, operation and schema transformers.</summary>
    public static void Configure(OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "BankingAI API",
                Version = "v1",
                Description = "Spending assistant backend on synthetic bank data. Every figure is computed here; a model only " +
                              "chooses tools and explains results. All endpoints except health and login need a bearer token."
            };
            // The address depends on where the API runs; a fixed document keeps the committed contract comparable.
            document.Servers = [];
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
            {
                [BearerScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
                    Description = "The token from POST /api/auth/login (60 minutes)."
                }
            };
            document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
            document.Components.Schemas[ProblemSchema] = Problem();
            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, _) =>
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;
            var isProtected = !metadata.OfType<IAllowAnonymous>().Any();
            var parameters = context.Description.ParameterDescriptions;

            // The XML summary is a whole paragraph; tools show the summary as a one-line title. The paragraph becomes the
            // description and the title is its first clause, without the "`GET /api/…`:" prefix the tool shows anyway.
            if (operation.Summary is { } text && operation.Description is null)
            {
                operation.Description = text;
                operation.Summary = ShortTitle(text);
            }

            // Only JSON is produced or read; the generator also lists text/plain and text/json.
            KeepJsonOnly(operation.RequestBody?.Content);
            if (operation.Responses is not null)
                foreach (var response in operation.Responses.Values)
                    KeepJsonOnly(response.Content);

            // The answers every endpoint can give, by rule; endpoint-specific ones (201, 409, 429, 503) are attributes.
            if (parameters.Count > 0)
                AddProblem(operation, context.Document!, "400", "Invalid input: a field-by-field list in errors.");
            if (isProtected)
                AddProblem(operation, context.Document!, "401", "No valid token (unauthorized) or the customer no longer exists (session_invalid).");
            if (parameters.Any(p => IdentifierParameters.Contains(p.Name)))
                AddProblem(operation, context.Document!, "404", "Not found; another customer's resource is answered the same way.");
            AddProblem(operation, context.Document!, "500", "Unexpected error, without any detail.");
            // Error statuses declared on an action (409, 429, 503) get the same problem body.
            foreach (var status in operation.Responses!.Keys.Where(k => k[0] is '4' or '5').ToList())
                AddProblem(operation, context.Document!, status, "Error.");

            if (isProtected)
                operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(BearerScheme, context.Document)] = [] }];
            return Task.CompletedTask;
        });

        options.AddSchemaTransformer((schema, context, _) =>
        {
            var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
            var nullable = (schema.Type & JsonSchemaType.Null) == JsonSchemaType.Null;
            if (type.IsEnum)
            {
                // StrictEnumConverter writes and reads exact names only; the generator does not know the converter.
                schema.Type = JsonSchemaType.String | (nullable ? JsonSchemaType.Null : 0);
                schema.Format = null;
                schema.Enum = Enum.GetNames(type).Select(name => (JsonNode)JsonValue.Create(name)!).ToList();
            }
            else if (type == typeof(decimal))
            {
                // Money: an exact JSON number (strict number handling), never a quoted string and never a double.
                schema.Type = JsonSchemaType.Number | (nullable ? JsonSchemaType.Null : 0);
                schema.Format = "decimal";
                schema.Pattern = null;
            }
            return Task.CompletedTask;
        });
    }

    private const int MaximumTitleLength = 80;

    private static string ShortTitle(string text)
    {
        // XML comments keep their source line breaks; a title is one line of words.
        var title = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (title.StartsWith('`') && title.IndexOf("`:", StringComparison.Ordinal) is var end and > 0)
            title = title[(end + 2)..].TrimStart();
        var stop = title.IndexOfAny(['.', ';', ':']);
        title = stop > 0 ? title[..stop].TrimEnd() : title;
        if (title.Length > MaximumTitleLength)
            title = title[..title.LastIndexOf(' ', MaximumTitleLength)] + "…";
        return title.Length == 0 ? text : char.ToUpperInvariant(title[0]) + title[1..];
    }

    private static void KeepJsonOnly(IDictionary<string, OpenApiMediaType>? content)
    {
        if (content is null)
            return;
        foreach (var mediaType in content.Keys.Where(k => k is not ("application/json" or ProblemJson)).ToList())
            content.Remove(mediaType);
    }

    private static void AddProblem(OpenApiOperation operation, OpenApiDocument document, string status, string description)
    {
        operation.Responses ??= new OpenApiResponses();
        operation.Responses[status] = new OpenApiResponse
        {
            Description = operation.Responses.TryGetValue(status, out var existing) && !string.IsNullOrEmpty(existing.Description)
                ? existing.Description
                : description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                [ProblemJson] = new OpenApiMediaType { Schema = new OpenApiSchemaReference(ProblemSchema, document) }
            }
        };
    }

    private static OpenApiSchema Problem() => new()
    {
        Type = JsonSchemaType.Object,
        Description = "Every error answer (API-13): a Turkish title, a fixed machine code and a trace id; never internals.",
        Properties = new Dictionary<string, IOpenApiSchema>
        {
            ["type"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["title"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Turkish, for people." },
            ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
            ["code"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Fixed machine code, e.g. validation_failed, session_invalid." },
            ["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String },
            ["errors"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Description = "Only on validation_failed: client field name → Turkish messages.",
                AdditionalProperties = new OpenApiSchema { Type = JsonSchemaType.Array, Items = new OpenApiSchema { Type = JsonSchemaType.String } }
            }
        },
        Required = new HashSet<string> { "title", "status", "code", "traceId" }
    };
}
