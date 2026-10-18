using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RuleLoom.Schemas;

namespace RuleLoom.Extensions.DependencyInjection;

/// <summary>
/// The schemas configured as sources in <see cref="RuleLoomBuilder.AddSchemas"/>, to be saved into the store.
/// </summary>
internal sealed class SchemaSources(IReadOnlyList<EventSchemaDefinition> schemas)
{
    public IReadOnlyList<EventSchemaDefinition> Schemas { get; } = schemas;
}

/// <summary>
/// Saves the schema sources into the configured store when the host starts.
/// </summary>
internal sealed class SchemaSourcesStartup(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => services.SaveRuleLoomSchemasAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
