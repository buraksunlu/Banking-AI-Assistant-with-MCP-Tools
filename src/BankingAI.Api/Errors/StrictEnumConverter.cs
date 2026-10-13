using System.Text.Json;
using System.Text.Json.Serialization;

namespace BankingAI.Api.Errors;

/// <summary>
/// Enums travel as their exact names ("Purchase"). Unlike the built-in converter, reading accepts neither numbers
/// nor other casings ("purchase"): input is never silently corrected, the same rule as "try" for a currency.
/// </summary>
public sealed class StrictEnumConverter : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert))!;

    private sealed class Converter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String && reader.GetString() is { } name && Enum.GetNames<T>().Contains(name))
                return Enum.Parse<T>(name);
            // The 400 body names the field; the message itself is never shown to the client.
            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
