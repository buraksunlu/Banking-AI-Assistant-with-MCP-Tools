using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace BankingAI.Api.Errors;

/// <summary>
/// Builds every 400 body: field names as the client sends them (camelCase JSON and query names), Turkish
/// messages, and no parser internals (type names, JSON paths, byte positions).
/// </summary>
public static class ApiValidation
{
    /// <summary>The client's name for a field: camelCase, and "body" for the request body as a whole.</summary>
    public static string FieldName(string name) => name.Length == 0 ? "body" : JsonNamingPolicy.CamelCase.ConvertName(name);

    /// <summary>
    /// The message of an input rule as written for the client: .NET appends " (Parameter 'X')" to an
    /// ArgumentException's message, which is removed.
    /// </summary>
    public static string Message(ArgumentException error)
    {
        var suffix = $" (Parameter '{error.ParamName}')";
        return error.ParamName is not null && error.Message.EndsWith(suffix, StringComparison.Ordinal)
            ? error.Message[..^suffix.Length]
            : error.Message;
    }

    /// <summary>
    /// Runs a shared input rule (<c>InputRules</c>) for a request property, so a request reuses the rule the
    /// database layer enforces instead of copying it. Returns the validation error, or none.
    /// </summary>
    public static IEnumerable<ValidationResult> Rule(Action rule, string member)
    {
        try
        {
            rule();
        }
        catch (ArgumentException error)
        {
            return [new ValidationResult(Message(error), [member])];
        }
        return [];
    }

    /// <summary>
    /// Replaces ASP.NET's automatic validation response (and <c>ValidationProblem()</c> in controllers).
    /// </summary>
    public static IActionResult InvalidModelState(ActionContext context)
    {
        var body = context.ActionDescriptor.Parameters
            .FirstOrDefault(p => p.BindingInfo?.BindingSource == BindingSource.Body);
        // JSON errors arrive as paths ("$.balance"); a property the request type does not have is unknown.
        var known = body?.ParameterType.GetProperties().Select(p => FieldName(p.Name)).ToHashSet() ?? [];
        var jsonFailed = context.ModelState.Keys.Any(k => k.StartsWith('$'));

        var errors = new ModelStateDictionary();
        foreach (var (key, entry) in context.ModelState)
        {
            if (entry.Errors.Count == 0)
                continue;
            if (key.StartsWith('$'))
            {
                var property = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..].Split('.', '[')[0] : null;
                if (property is null or "")
                    Add(errors, "body", "Geçersiz JSON.");
                else
                    Add(errors, property, known.Contains(property) ? "Geçersiz değer." : "Tanınmayan alan.");
            }
            else if (body is not null && (key.Length == 0 || key == body.Name))
            {
                // The body parameter's own "required" error: noise next to a JSON error, otherwise an empty body.
                if (!jsonFailed)
                    Add(errors, "body", "İstek gövdesi zorunlu.");
            }
            else
            {
                foreach (var error in entry.Errors)
                    Add(errors, FieldName(key), error.ErrorMessage);
            }
        }

        var problem = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>()
            .CreateValidationProblemDetails(context.HttpContext, errors, StatusCodes.Status400BadRequest);
        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    }

    /// <summary>Turkish texts for the errors model binding itself produces (route, query and body values).</summary>
    public static void UseTurkishMessages(DefaultModelBindingMessageProvider messages)
    {
        messages.SetAttemptedValueIsInvalidAccessor((value, _) => $"'{value}' geçerli bir değer değil.");
        messages.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"'{value}' geçerli bir değer değil.");
        messages.SetValueIsInvalidAccessor(value => $"'{value}' geçerli bir değer değil.");
        messages.SetUnknownValueIsInvalidAccessor(_ => "Geçerli bir değer değil.");
        messages.SetNonPropertyUnknownValueIsInvalidAccessor(() => "Geçerli bir değer değil.");
        messages.SetValueMustBeANumberAccessor(_ => "Sayı olmalı.");
        messages.SetNonPropertyValueMustBeANumberAccessor(() => "Sayı olmalı.");
        messages.SetValueMustNotBeNullAccessor(_ => "Boş olamaz.");
        messages.SetMissingBindRequiredValueAccessor(_ => "Zorunlu alan.");
        messages.SetMissingKeyOrValueAccessor(() => "Zorunlu alan.");
        messages.SetMissingRequestBodyRequiredValueAccessor(() => "İstek gövdesi zorunlu.");
    }

    private static void Add(ModelStateDictionary errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var entry) || entry.Errors.All(e => e.ErrorMessage != message))
            errors.AddModelError(key, message);
    }
}
