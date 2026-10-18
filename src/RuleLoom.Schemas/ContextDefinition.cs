namespace RuleLoom.Schemas;

/// <summary>
/// A context the event relates to: an entity rules can use with <c>@name</c> paths, loaded by the context provider
/// registered under <paramref name="Provider"/>. Its fields are described where the provider is registered.
/// </summary>
/// <param name="Name">The context name (matched ignoring case).</param>
/// <param name="Label">A readable name for authoring UIs, if configured (<c>title</c>).</param>
/// <param name="Description">An optional longer explanation.</param>
/// <param name="ReferencedBy">How the event refers to the context, or <c>null</c> when the provider needs no reference
/// (e.g. tenant settings).</param>
/// <param name="Provider">The name of the context provider that loads it (<c>LoadedBy</c>); the context's own name when
/// not configured.</param>
public sealed record ContextDefinition(string Name, string? Label, string? Description, ContextReference? ReferencedBy, string Provider);
