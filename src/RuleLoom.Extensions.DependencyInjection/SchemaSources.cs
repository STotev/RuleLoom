using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RuleLoom.Abstractions.Context;
using RuleLoom.Schemas;

namespace RuleLoom.Extensions.DependencyInjection;

/// <summary>
/// The schemas configured in <see cref="RuleLoomBuilder.AddSchemas"/> and the contexts the registered providers describe,
/// to be saved into the store.
/// </summary>
internal sealed class SchemaSources(SchemaSet configured, JsonSerializerOptions? options, RuleLoomRegistry registry)
{
    /// <summary>
    /// The configured schemas plus each registered provider's schema. Providers are Scoped services, so they're resolved
    /// from <paramref name="scope"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">An event type or context name is described twice, or a provider's
    /// description is invalid.</exception>
    public SchemaSet Collect(IServiceProvider scope)
    {
        var described = registry.ContextProviders
            .Select(provider => ((IContextProvider)scope.GetRequiredService(provider.Value)).GetSchema(provider.Key, options))
            .OfType<ContextSchemaDefinition>();

        return new SchemaSet(configured.Events, configured.Contexts.Concat(described));
    }
}

/// <summary>
/// Saves the schema sources into the configured store when the host starts.
/// </summary>
internal sealed class SchemaSourcesStartup(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => services.SaveRuleLoomSchemasAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
