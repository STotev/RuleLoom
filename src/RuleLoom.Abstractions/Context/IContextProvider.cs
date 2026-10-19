using System.Text.Json;
using RuleLoom.Schemas;

namespace RuleLoom.Abstractions.Context;

/// <summary>
/// Loads a context: data related to an event's subject that rules use with <c>@name</c> paths (e.g. the order's
/// customer for <c>@customer.Tier</c>). Registered under a name; the engine calls it only when a rule or action uses
/// that context, at most once per event.
/// </summary>
public interface IContextProvider
{
    /// <summary>
    /// Loads the context for the event in <paramref name="request"/>.
    /// </summary>
    /// <param name="request">The context name and the event (type, current and previous state).</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The context (a .NET object, JSON or a collection), or <c>null</c> if there is none: paths into it
    /// then resolve to <c>null</c>.</returns>
    ValueTask<object?> GetAsync(ContextRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Describes the context's fields for validation and authoring UIs, as registered under <paramref name="name"/>.
    /// <see cref="ContextProvider{TContext}"/> and <see cref="CollectionContextProvider{TItem}"/> describe their type;
    /// other providers have no schema.
    /// </summary>
    /// <param name="name">The name the provider is registered under.</param>
    /// <param name="options">The serializer options fields are named with; Web defaults when omitted.</param>
    /// <returns>The context's schema, or <c>null</c> when the provider doesn't describe one.</returns>
    ContextSchemaDefinition? GetSchema(string name, JsonSerializerOptions? options = null) => null;
}
