using System.Collections.Concurrent;
using RuleLoom.Abstractions.Context;

namespace RuleLoom.Samples.AspNetCore.Orders;

/// <summary>The shop's orders; a real app has a database.</summary>
public sealed class OrderStore
{
    private readonly ConcurrentDictionary<string, Order> _orders = new();
    private int _lastNumber = 1000;

    public string NextNumber() => $"SO-{Interlocked.Increment(ref _lastNumber)}";

    public void Save(Order order) => _orders[order.Number] = order;

    public Order? Find(string number) => _orders.GetValueOrDefault(number);

    public IReadOnlyList<Order> All() => _orders.Values.OrderByDescending(o => o.Number).ToList();
}

/// <summary>
/// Registered as "order": loads an order as it is now. Order events declare it as their subject's loader, so a
/// scheduled action is checked against the order when it's due, not as it was when it was scheduled.
/// </summary>
public sealed class OrderProvider(OrderStore orders) : ContextProvider<Order>
{
    protected override ValueTask<Order?> LoadAsync(ContextRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(orders.Find(request.GetReference<string>()!));
}
