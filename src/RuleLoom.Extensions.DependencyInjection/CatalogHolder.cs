using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RuleLoom.Abstractions;
using RuleLoom.Engine;

namespace RuleLoom.Extensions.DependencyInjection;

/// <summary>
/// The service collection's catalog: the builder <c>AddCatalog</c> configures, and the catalog built from it at startup.
/// Scopes resolve <see cref="Catalog"/> from <see cref="Current"/>, so a rebuilt catalog could replace it later. Also the
/// engine's options, which <c>Configure</c> sets.
/// </summary>
internal sealed class CatalogHolder
{
    private Catalog? _current;

    public CatalogBuilder Builder { get; } = new();

    public RuleEngineOptions EngineOptions { get; } = new();

    public Catalog? Current
    {
        get => Volatile.Read(ref _current);
        set => Volatile.Write(ref _current, value);
    }
}

/// <summary>Builds the catalog when the host starts.</summary>
internal sealed class CatalogStartup(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => services.StartRuleLoomAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
