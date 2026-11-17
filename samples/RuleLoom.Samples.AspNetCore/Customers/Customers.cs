using RuleLoom.Abstractions.Context;
using RuleLoom.Schemas;

namespace RuleLoom.Samples.AspNetCore.Customers;

// The contexts order events declare: what the shop knows about a customer, loaded only when a rule or an action asks.

public sealed record CustomerProfile(string Name, string Email, string Tier, DateOnly MemberSince);

public sealed record PastOrder(string Number, decimal Total, DateOnly PlacedOn);

/// <summary>Describes <see cref="CustomerProfile"/> to rule authors; found next to the type by the catalog.</summary>
public sealed class CustomerProfileSchema : ContextSchema<CustomerProfile>
{
    public CustomerProfileSchema()
    {
        Label("Customer");
        Description("The customer's loyalty profile.");
        Field(c => c.Tier).Label("Loyalty tier").Description("bronze, silver or gold");
        Field(c => c.MemberSince).Label("Member since");
    }
}

/// <summary>The shop's customers; a real app has a database. Ada is gold with three earlier orders, Bob bronze with none.</summary>
public sealed class CustomerDirectory
{
    private readonly Dictionary<string, CustomerProfile> _profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ada@example.com"] = new("Ada", "ada@example.com", "gold", new DateOnly(2021, 3, 14)),
        ["bob@example.com"] = new("Bob", "bob@example.com", "bronze", new DateOnly(2026, 9, 1))
    };

    private readonly Dictionary<string, PastOrder[]> _orders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ada@example.com"] =
        [
            new("SO-0871", 120.00m, new DateOnly(2026, 2, 3)),
            new("SO-0934", 89.90m, new DateOnly(2026, 6, 21)),
            new("SO-0988", 410.00m, new DateOnly(2026, 9, 30))
        ]
    };

    public IReadOnlyCollection<CustomerProfile> All => _profiles.Values;

    public CustomerProfile? FindProfile(string email) => _profiles.GetValueOrDefault(email);

    public IReadOnlyList<PastOrder> FindOrders(string email) => _orders.GetValueOrDefault(email) ?? [];
}

/// <summary>Registered as "customer": the profile of the customer an order refers to (by email), or <c>null</c> for a guest.</summary>
public sealed class CustomerProvider(CustomerDirectory customers) : ContextProvider<CustomerProfile>
{
    protected override ValueTask<CustomerProfile?> LoadAsync(ContextRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(customers.FindProfile(request.GetReference<string>()!));
}

/// <summary>Registered as "orderHistory": the customer's earlier orders.</summary>
public sealed class OrderHistoryProvider(CustomerDirectory customers) : CollectionContextProvider<PastOrder>
{
    protected override ValueTask<IReadOnlyList<PastOrder>> LoadAsync(ContextRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(customers.FindOrders(request.GetReference<string>()!));
}
