using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RuleLoom.Extensions.DependencyInjection;
using RuleLoom.Schemas;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Startup tasks for RuleLoom registered with <c>AddRuleLoom</c>.
/// </summary>
public static partial class RuleLoomServiceProviderExtensions
{
    /// <summary>
    /// Builds the catalog configured in <c>AddRuleLoom</c>: from code, or from code plus the configured
    /// <see cref="ICatalogStore"/>, which then gets the result saved back. Hosted apps (ASP.NET Core, worker services)
    /// do this at startup automatically; call it yourself in apps without a host. Does nothing once the catalog is
    /// built.
    /// </summary>
    /// <remarks>Runs in its own scope, so a scoped store sees no request.</remarks>
    /// <exception cref="InvalidOperationException"><c>AddRuleLoom</c> wasn't called, or the catalog is broken.</exception>
    public static async Task StartRuleLoomAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var holder = services.GetService<CatalogHolder>()
            ?? throw new InvalidOperationException("RuleLoom isn't registered: call services.AddRuleLoom(...) first.");

        if (holder.Current is not null)
            return;

        await using var scope = services.CreateAsyncScope();

        var catalog = scope.ServiceProvider.GetService<ICatalogStore>() is { } store
            ? await holder.Builder.BuildAsync(store, cancellationToken).ConfigureAwait(false)
            : holder.Builder.Build();

        var logger = (services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance).CreateLogger("RuleLoom.Catalog");
        LogCatalogBuilt(logger, catalog.Events.Count, catalog.ContextProviders.Count, catalog.Operators.Count, catalog.Actions.Count,
            catalog.TemporalRules.TimeZone.Id);
        foreach (var problem in catalog.Problems)
            LogCatalogProblem(logger, problem.Kind, problem.Name, problem.Message);

        holder.Current = catalog;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "RuleLoom catalog built: {Events} event types, {ContextProviders} context providers, {Operators} operators, {Actions} actions; time zone {TimeZone}")]
    private static partial void LogCatalogBuilt(ILogger logger, int events, int contextProviders, int operators, int actions, string timeZone);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RuleLoom catalog problem in {Kind} '{Name}': {Message}")]
    private static partial void LogCatalogProblem(ILogger logger, CatalogEntryKind kind, string name, string message);
}
