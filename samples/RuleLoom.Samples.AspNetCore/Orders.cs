using System.Collections.Concurrent;
using RuleLoom.Abstractions.Context;

namespace RuleLoom.Samples.AspNetCore;

/// <summary>
/// Stands in for the shop's order database: the producer updates it before it sends an event, so the sample saves each
/// order event's current state here (see <c>POST /events</c>). Keyed by tenant and order number.
/// </summary>
public sealed class OrderStore
{
    private readonly ConcurrentDictionary<(string Tenant, string Number), Order> _orders = new();

    public void Save(string tenantId, Order order) => _orders[(tenantId, order.Number)] = order;

    public Order? Find(string tenantId, string number) => _orders.GetValueOrDefault((tenantId, number));
}

/// <summary>
/// Registered as "order": loads an order as it is now, the subject of order events (<c>Subject(o =&gt; o.Number)
/// .LoadedBy("order")</c>), so a scheduled action is checked against current data when it's due. The tenant comes from
/// the event's metadata.
/// </summary>
public sealed class OrderProvider(OrderStore orders) : ContextProvider<Order>
{
    protected override ValueTask<Order?> LoadAsync(ContextRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(request.Event?.Metadata.GetValueOrDefault("tenantId") is string tenantId
            ? orders.Find(tenantId, request.GetReference<string>()!)
            : null);
}
