using RuleLoom.Extensions.DependencyInjection;
using RuleLoom.Schemas;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Startup tasks for RuleLoom registered with <c>AddRuleLoom</c>.
/// </summary>
public static class RuleLoomServiceProviderExtensions
{
    /// <summary>
    /// Saves the schema sources configured in <c>AddSchemas</c> (event and context schemas) into the configured
    /// <see cref="ISchemaStore"/>, replacing stored schemas of the same event type or context. Hosted apps (ASP.NET Core, worker services) do this at startup
    /// automatically; call it yourself in apps without a host. Does nothing when no schemas are registered.
    /// </summary>
    /// <remarks>Runs in its own scope, so a scoped (e.g. tenant-aware) store sees no request or tenant: sources are
    /// schemas defined in code, shared by every tenant.</remarks>
    public static async Task SaveRuleLoomSchemasAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.GetService<SchemaSources>()?.Schemas is not { } schemas || schemas.Events.Count + schemas.Contexts.Count == 0)
            return;

        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ISchemaStore>();

        foreach (var schema in schemas.Events)
            await store.SaveEventAsync(schema, cancellationToken).ConfigureAwait(false);
        foreach (var schema in schemas.Contexts)
            await store.SaveContextAsync(schema, cancellationToken).ConfigureAwait(false);
    }
}
