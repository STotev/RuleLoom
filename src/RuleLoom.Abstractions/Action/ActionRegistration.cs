using RuleLoom.Schemas;

namespace RuleLoom.Abstractions.Action;

/// <summary>
/// An action in a <see cref="Catalog"/>: the name rules use, the type an <see cref="IActionFactory"/> creates, and the
/// description from its <see cref="ActionDescriptor{TAction}"/>.
/// </summary>
/// <param name="Name">The name rules use (matched ignoring case).</param>
/// <param name="Type">The action's type, an <see cref="IAction"/>.</param>
/// <param name="Label">A readable name for authoring UIs, if described.</param>
/// <param name="Description">An optional longer explanation.</param>
/// <param name="Parameters">The parameters of an <see cref="IAction{TParameters}"/>; <c>null</c> for other actions.</param>
public sealed record ActionRegistration(
    string Name, Type Type, string? Label = null, string? Description = null, ParametersSchemaDefinition? Parameters = null);
