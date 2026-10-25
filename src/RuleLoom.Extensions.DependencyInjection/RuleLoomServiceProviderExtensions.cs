using RuleLoom.Extensions.DependencyInjection;
using RuleLoom.Schemas;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Startup tasks for RuleLoom registered with <c>AddRuleLoom</c>.
/// </summary>
public static class RuleLoomServiceProviderExtensions
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

        holder.Current = scope.ServiceProvider.GetService<ICatalogStore>() is { } store
            ? await holder.Builder.BuildAsync(store, cancellationToken).ConfigureAwait(false)
            : holder.Builder.Build();
    }
}
