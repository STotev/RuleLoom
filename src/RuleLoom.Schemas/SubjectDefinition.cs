namespace RuleLoom.Schemas;

/// <summary>
/// An event's subject, the entity it's about (declared with <c>Subject(...)</c>): how its identity is read from the
/// event, and the context provider that loads it as it is now, if any. Written as <c>x-ruleloom-subject</c>.
/// </summary>
/// <param name="Identity">The subject's identity: one subject path (e.g. <c>number</c>) or named paths for a composite
/// identity, in the order they're declared (the same shape as a context's reference).</param>
/// <param name="Provider">The context provider that loads the subject (<c>LoadedBy</c>); <c>null</c> when it isn't
/// loaded, and scheduled actions run against the stored event.</param>
public sealed record SubjectDefinition(ContextReference Identity, string? Provider);
