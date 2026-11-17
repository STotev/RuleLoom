using RuleLoom.Abstractions.Context;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Actions;
using RuleLoom.Contexts;
using RuleLoom.Engine;
using RuleLoom.Schemas;

namespace RuleLoom.Samples.Console.Scenarios;

/// <summary>
/// Rules can read data the event doesn't carry. The event's schema declares a context ("customer", found by the
/// customer's email); rules read it as "@customer.Tier"; a context provider loads it, only when a rule needs it.
/// </summary>
public static class Contexts
{
    private const string GoldCustomer = """
    {
      "id": "gold-customer",
      "name": "Gold customer",
      "eventType": "OrderPlaced",
      "condition": { "operator": "eq", "operands": [{ "path": "@customer.Tier" }, { "value": "gold" }] },
      "actions": [{ "action": "console-log", "parameters": { "message": "Gold customer: add a gift" } }]
    }
    """;

    public static async Task RunAsync()
    {
        Show.Scenario(3, "Contexts", "A rule reads the customer's loyalty tier, which the app loads when the rule asks.");
        Show.Json("Rule", GoldCustomer);

        var catalog = new CatalogBuilder()
            .AddEvent<OrderPlacedWithCustomerSchema>()
            .AddContextProvider<CustomerProvider>("customer")
            .AddAction<ConsoleLogAction>()
            .Build();
        var engine = new RuleEngine(catalog, contextProviders: new DirectContextProviderFactory().Add(new CustomerProvider()));
        var rule = RuleJsonParser.Parse(GoldCustomer);

        Show.Step("Ada (gold) places an order");
        Show.Result(await engine.ExecuteAsync("OrderPlaced", new Order("SO-1001", OrderStatus.AwaitingPayment, 640.50m, Customers.Ada), [rule]));

        Show.Step("Bob (bronze) places an order");
        Show.Result(await engine.ExecuteAsync("OrderPlaced", new Order("SO-1002", OrderStatus.AwaitingPayment, 75.00m, Customers.Bob), [rule]));
    }
}

/// <summary>"OrderPlaced", declaring the customer's profile as the "customer" context, found by the customer's email.</summary>
public sealed class OrderPlacedWithCustomerSchema : EventSchema<Order>
{
    public OrderPlacedWithCustomerSchema()
    {
        Event("OrderPlaced").Label("Order placed");
        Context("customer").ReferencedBy(o => o.Customer.Email).Label("Customer");
    }
}

/// <summary>What the shop knows about a customer, kept outside the order.</summary>
public sealed record CustomerProfile(string Email, string Tier);

/// <summary>Loads a customer's profile by email (the reference the schema declares); a real app reads its database.</summary>
public sealed class CustomerProvider : ContextProvider<CustomerProfile>
{
    private static readonly Dictionary<string, CustomerProfile> Profiles = new()
    {
        [Customers.Ada.Email] = new(Customers.Ada.Email, "gold"),
        [Customers.Bob.Email] = new(Customers.Bob.Email, "bronze")
    };

    protected override ValueTask<CustomerProfile?> LoadAsync(ContextRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Profiles.GetValueOrDefault(request.GetReference<string>()!));
}
