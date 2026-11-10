using System.Text.Json;
using RuleLoom.Abstractions.Values;

namespace RuleLoom.Abstractions.Context;

/// <summary>
/// What an <see cref="IContextProvider"/> gets: the context's name, the event it's loaded for and, when the event
/// declares one, the resolved reference to the context.
/// </summary>
/// <param name="Name">The context name as registered (e.g. <c>customer</c>).</param>
/// <param name="EventType">The event being processed.</param>
/// <param name="Current">The subject's current state.</param>
/// <param name="Previous">The subject's previous state, if any.</param>
public sealed record ContextRequest(string Name, string EventType, object? Current, object? Previous)
{
    // Composite references map onto typed keys like JSON does: property names ignore case.
    private static readonly JsonSerializerOptions ReferenceJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The event's reference to this context, resolved from the subject by the engine when the event's schema declares one
    /// (<c>ReferencedBy</c>): the value for a single reference (e.g. a
    /// customer ID), the part values by name for a composite one (<see cref="IReadOnlyDictionary{TKey, TValue}"/>), or
    /// <c>null</c> when no reference is declared (read the subject with <see cref="Resolve"/> instead).
    /// </summary>
    public object? Reference { get; init; }

    /// <summary>How date-times are read: the catalog's time zone (<see cref="GetReference{T}"/> converts with it).</summary>
    public TemporalRules TemporalRules { get; init; } = TemporalRules.Utc;

    /// <summary>
    /// Resolves a path on the subject (e.g. <c>CustomerId</c> or <c>$previous.CustomerId</c>), with the same rules as
    /// rule paths. Contexts can't be resolved from here.
    /// </summary>
    public object? Resolve(string path) => new EvaluationContext(Current, Previous).Resolve(path);

    /// <summary>
    /// Gets <see cref="Reference"/> as <typeparamref name="T"/>: a single value converted like rule values (e.g. text to a
    /// <see cref="Guid"/> or a number; any value to its invariant text for <see cref="string"/>), or the parts of a composite reference mapped onto <typeparamref name="T"/>'s
    /// properties by name, ignoring case (e.g. <c>record ZoneKey(string Country, string Postcode)</c>).
    /// </summary>
    /// <returns>The reference, or the default of <typeparamref name="T"/> when there is none.</returns>
    /// <exception cref="InvalidCastException">The reference can't be converted to <typeparamref name="T"/>.</exception>
    public T? GetReference<T>() => Reference switch
    {
        null => default,
        T typed => typed,
        IReadOnlyDictionary<string, object?> parts => JsonSerializer.SerializeToElement(parts, ReferenceJsonOptions).Deserialize<T>(ReferenceJsonOptions),
        // Keys are often read as text: numbers, GUIDs and other values convert to their invariant string form.
        string text when typeof(T) == typeof(Guid) && Guid.TryParse(text, out var id) => (T)(object)id,
        IFormattable formattable when typeof(T) == typeof(string) => (T)(object)formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        var value => ValueConverter.TryConvert<T>(value, TemporalRules, out var converted)
            ? converted
            : throw new InvalidCastException($"The reference to context '{Name}' ({value}) can't be converted to {typeof(T).Name}.")
    };
}
