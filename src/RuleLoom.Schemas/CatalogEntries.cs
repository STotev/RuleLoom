namespace RuleLoom.Schemas;

/// <summary>
/// An operator in a <see cref="CatalogDocument"/>: what rule conditions can use.
/// </summary>
/// <param name="Name">The name rules use (matched ignoring case).</param>
/// <param name="Label">A readable name for authoring UIs, if the operator has one.</param>
/// <param name="Description">An optional longer explanation.</param>
/// <param name="MinOperands">The fewest operands it takes.</param>
/// <param name="MaxOperands">The most operands it takes; <c>null</c> when unlimited.</param>
public sealed record CatalogOperator(string Name, string? Label, string? Description, int MinOperands, int? MaxOperands);

/// <summary>
/// An action in a <see cref="CatalogDocument"/>: what rules can run.
/// </summary>
/// <param name="Name">The name rules use (matched ignoring case).</param>
/// <param name="Label">A readable name for authoring UIs, if the action's descriptor gives one.</param>
/// <param name="Description">An optional longer explanation.</param>
public sealed record CatalogAction(string Name, string? Label, string? Description);

/// <summary>
/// The kind of catalog entry a <see cref="CatalogProblem"/> is about.
/// </summary>
public enum CatalogEntryKind
{
    /// <summary>An event schema.</summary>
    Event,

    /// <summary>A context schema.</summary>
    Context
}

/// <summary>
/// A broken link in an entry that a catalog store contributed (e.g. a context with no provider). It affects only the
/// rules that use the entry; the same mistake in code fails the catalog's build instead.
/// </summary>
/// <param name="Kind">The kind of entry.</param>
/// <param name="Name">The entry's event type or context name.</param>
/// <param name="Message">What's wrong.</param>
public sealed record CatalogProblem(CatalogEntryKind Kind, string Name, string Message);
