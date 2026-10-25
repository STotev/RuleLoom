namespace RuleLoom.Schemas;

/// <summary>
/// A field of an event's subject or of a context.
/// </summary>
/// <param name="Name">The field's JSON name (rule paths match it ignoring case); empty for array items.</param>
/// <param name="Type">The kind of value.</param>
/// <param name="IsNullable">Whether the field can be <c>null</c>.</param>
/// <param name="Label">A readable name for authoring UIs, if configured (<c>title</c>).</param>
/// <param name="Description">An optional longer explanation.</param>
/// <param name="EnumValues">The allowed values when <paramref name="Type"/> is <see cref="FieldType.Enum"/>.</param>
/// <param name="Fields">Nested fields when <paramref name="Type"/> is <see cref="FieldType.Object"/>.</param>
/// <param name="Items">The element description when <paramref name="Type"/> is <see cref="FieldType.Array"/>.</param>
public sealed record FieldDefinition(
    string Name,
    FieldType Type,
    bool IsNullable,
    string? Label,
    string? Description,
    IReadOnlyList<string> EnumValues,
    IReadOnlyList<FieldDefinition> Fields,
    FieldDefinition? Items)
{
    /// <summary>Compares every member, including the contents of <see cref="EnumValues"/> and <see cref="Fields"/>.</summary>
    public bool Equals(FieldDefinition? other) =>
        other is not null &&
        (Name, Type, IsNullable, Label, Description) == (other.Name, other.Type, other.IsNullable, other.Label, other.Description) &&
        EnumValues.SequenceEqual(other.EnumValues) &&
        Fields.SequenceEqual(other.Fields) &&
        Equals(Items, other.Items);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Name, Type, IsNullable, Label, Description, EnumValues.Count, Fields.Count);

    /// <summary>
    /// Finds the field a path segment names, like property paths do: an exact-case match wins; otherwise a single
    /// match ignoring case; an ambiguous match is <c>null</c>.
    /// </summary>
    public static FieldDefinition? Find(IReadOnlyList<FieldDefinition> fields, string name)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(name);

        if (fields.FirstOrDefault(f => f.Name == name) is { } exact)
            return exact;

        var matches = fields.Where(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
}
