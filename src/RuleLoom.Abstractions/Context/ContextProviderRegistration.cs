using RuleLoom.Schemas;

namespace RuleLoom.Abstractions.Context;

/// <summary>
/// A context provider in a <see cref="Catalog"/>: the name events refer to it by (<c>LoadedBy</c>, or a context of
/// the same name), the type an <see cref="IContextProviderFactory"/> creates, and the schema of what it loads.
/// </summary>
/// <param name="Name">The name it's registered under (matched ignoring case).</param>
/// <param name="Type">The provider's type, an <see cref="IContextProvider"/>.</param>
/// <param name="Schema">The schema of what it loads, or <c>null</c> when it has none.</param>
public sealed record ContextProviderRegistration(string Name, Type Type, ContextSchemaDefinition? Schema = null);
