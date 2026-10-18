namespace RuleLoom.Schemas;

/// <summary>
/// A context an event's rules can use with <c>@name</c> paths: related data loaded by a context provider.
/// </summary>
/// <param name="Name">The context name (matched ignoring case).</param>
/// <param name="Label">A readable name for authoring UIs, if configured (<c>title</c>).</param>
/// <param name="Description">An optional longer explanation.</param>
/// <param name="IsMany">Whether the context is a collection (its <paramref name="Fields"/> describe one element).</param>
/// <param name="Fields">The context's fields (of one element for collections).</param>
public sealed record ContextDefinition(
    string Name,
    string? Label,
    string? Description,
    bool IsMany,
    IReadOnlyList<FieldDefinition> Fields);
