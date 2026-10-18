namespace RuleLoom.Schemas;

/// <summary>
/// How an event refers to a context: one subject path (e.g. <c>customerId</c>) or named paths for a composite reference
/// (e.g. <c>country</c> → <c>address.country</c>, <c>postcode</c> → <c>address.postcode</c>). Paths are JSON paths of
/// the subject.
/// </summary>
/// <param name="Path">The subject path of a single reference; <c>null</c> for a composite one.</param>
/// <param name="Parts">The named subject paths of a composite reference; empty for a single one.</param>
public sealed record ContextReference(string? Path, IReadOnlyDictionary<string, string> Parts)
{
    /// <summary>Whether the reference has several named parts.</summary>
    public bool IsComposite => Path is null;
}
