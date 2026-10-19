using RuleLoom.Extensions.DependencyInjection;
using RuleLoom.Schemas;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Startup tasks for RuleLoom registered with <c>AddRuleLoom</c>.
/// </summary>
public static class RuleLoomServiceProviderExtensions
{
    /// <summary>
    /// Saves the schema sources configured in <c>AddSchemas</c> (event schemas, and the contexts registered providers
    /// describe) into the configured <see cref="ISchemaStore"/>, replacing stored schemas of the same event type or
    /// context. Hosted apps (ASP.NET Core, worker services) do this at startup automatically; call it yourself in apps
    /// without a host. Does nothing when <c>AddSchemas</c> isn't called or there are no schemas.
    /// </summary>
    /// <remarks>Runs in its own scope, so a scoped (e.g. tenant-aware) store sees no request or tenant: sources are
    /// schemas defined in code, shared by every tenant.</remarks>
    /// <exception cref="InvalidOperationException">A context is described twice, or a provider's description is
    /// invalid.</exception>
    public static async Task SaveRuleLoomSchemasAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.GetService<SchemaSources>() is not { } sources)
            return;

        await using var scope = services.CreateAsyncScope();
        var schemas = sources.Collect(scope.ServiceProvider);
        if (schemas.Events.Count + schemas.Contexts.Count == 0)
            return;

        var store = scope.ServiceProvider.GetRequiredService<ISchemaStore>();

        foreach (var schema in schemas.Events)
            await store.SaveEventAsync(schema, cancellationToken).ConfigureAwait(false);
        foreach (var schema in schemas.Contexts)
            await store.SaveContextAsync(schema, cancellationToken).ConfigureAwait(false);
    }
}
