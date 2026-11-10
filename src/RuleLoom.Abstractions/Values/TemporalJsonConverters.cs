using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using RuleLoom.Schemas;

namespace RuleLoom.Abstractions.Values;

/// <summary>
/// Reads <see cref="DateTimeOffset"/> and <see cref="DateTime"/> when binding parameters with a
/// <see cref="TemporalRules"/>, instead of System.Text.Json's own reading (which takes text without an offset as the
/// machine's local time). A <see cref="DateTime"/> is read in UTC.
/// </summary>
internal static class TemporalJsonConverters
{
    private static readonly ConditionalWeakTable<TemporalRules, JsonSerializerOptions> Cache = new();

    /// <summary><see cref="ParametersSchemaDefinition.JsonOptions"/> with date-times read by <paramref name="rules"/>.</summary>
    public static JsonSerializerOptions Options(TemporalRules rules) => Cache.GetValue(rules, static rules =>
    {
        var options = new JsonSerializerOptions(ParametersSchemaDefinition.JsonOptions);
        options.Converters.Add(new DateTimeOffsetConverter(rules));
        options.Converters.Add(new DateTimeConverter(rules));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    });

    private static DateTimeOffset Read(ref Utf8JsonReader reader, TemporalRules rules) =>
        reader.TokenType == JsonTokenType.String && rules.TryGetInstant(reader.GetString(), out var instant)
            ? instant
            : throw new JsonException("Expected an ISO 8601 date-time.");

    private sealed class DateTimeOffsetConverter(TemporalRules rules) : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            TemporalJsonConverters.Read(ref reader, rules);

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }

    private sealed class DateTimeConverter(TemporalRules rules) : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            TemporalJsonConverters.Read(ref reader, rules).UtcDateTime;

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }
}
