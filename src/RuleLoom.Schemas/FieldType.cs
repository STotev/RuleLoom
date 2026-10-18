namespace RuleLoom.Schemas;

/// <summary>
/// The kind of value a field holds, as described by its JSON Schema.
/// </summary>
public enum FieldType
{
    /// <summary>Text (<c>"type": "string"</c>), including formats other than dates such as <c>uuid</c>.</summary>
    String,

    /// <summary>A whole number (<c>"type": "integer"</c>).</summary>
    Integer,

    /// <summary>A number (<c>"type": "number"</c>).</summary>
    Number,

    /// <summary><c>true</c> or <c>false</c>.</summary>
    Boolean,

    /// <summary>An ISO 8601 date and time (<c>"format": "date-time"</c>).</summary>
    DateTime,

    /// <summary>An ISO 8601 date (<c>"format": "date"</c>).</summary>
    Date,

    /// <summary>One of a fixed set of values (<see cref="FieldDefinition.EnumValues"/>).</summary>
    Enum,

    /// <summary>An object with nested <see cref="FieldDefinition.Fields"/> (none for dictionaries and cycles).</summary>
    Object,

    /// <summary>A list of values described by <see cref="FieldDefinition.Items"/>.</summary>
    Array,

    /// <summary>Any JSON value (an <c>object</c>, <c>JsonElement</c> or <c>JsonNode</c> property; the schema <c>true</c>).</summary>
    Any
}
