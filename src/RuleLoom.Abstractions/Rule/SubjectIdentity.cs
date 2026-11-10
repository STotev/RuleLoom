using System.Globalization;

namespace RuleLoom.Abstractions.Rule;

/// <summary>
/// An event's subject identity as text, the last part of a <see cref="ScheduledAction.Key"/>: one value
/// (<c>order-1001</c>), or the parts of a composite identity in their declared order (<c>country=BG;postcode=1000</c>),
/// with values in invariant culture and <c>\</c>, <c>:</c>, <c>;</c> and <c>=</c> escaped by a <c>\</c>.
/// </summary>
public sealed class SubjectIdentity
{
    private SubjectIdentity(string text) => Text = text;

    /// <summary>The identity as it appears in keys.</summary>
    public string Text { get; }

    /// <summary>One value, e.g. an order number; <c>null</c> when the value is.</summary>
    public static SubjectIdentity? From(object? value) =>
        Format(value) is { } text ? new SubjectIdentity(ScheduledAction.Escape(text)) : null;

    /// <summary>The named parts of a composite identity, in order; <c>null</c> when any part is.</summary>
    public static SubjectIdentity? From(IEnumerable<KeyValuePair<string, object?>> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var texts = new List<string>();
        foreach (var (name, value) in parts)
        {
            if (Format(value) is not { } text)
                return null;

            texts.Add($"{ScheduledAction.Escape(name)}={ScheduledAction.Escape(text)}");
        }

        return new SubjectIdentity(string.Join(';', texts));
    }

    /// <inheritdoc />
    public override string ToString() => Text;

    private static string? Format(object? value) => value switch
    {
        null => null,
        string text => text,
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };
}
