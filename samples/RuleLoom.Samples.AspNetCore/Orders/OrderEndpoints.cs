using RuleLoom.Abstractions;
using RuleLoom.Samples.AspNetCore.Customers;
using RuleLoom.Samples.AspNetCore.Rules;

namespace RuleLoom.Samples.AspNetCore.Orders;

/// <summary>What a customer sends to place an order. Payment is due a minute later by default, so a demo stays short.</summary>
public sealed record PlaceOrder(string CustomerEmail, decimal Total, int ItemCount = 1, int PaymentDueInSeconds = 60);

/// <summary>
/// The shop's own API. Each change saves the order and raises an event (the order before and after) to the rules,
/// in-process; the response is the order and what the rules did.
/// </summary>
public static class OrderEndpoints
{
    public static void MapOrders(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/orders");

        orders.MapGet("/", (OrderStore store) => store.All());

        orders.MapPost("/", async (PlaceOrder request, OrderStore store, CustomerDirectory customers, RuleRunner rules,
            TimeProvider time, CancellationToken cancellationToken) =>
        {
            var customer = new Customer(customers.FindProfile(request.CustomerEmail)?.Name ?? "Guest", request.CustomerEmail);
            var order = new Order(store.NextNumber(), OrderStatus.AwaitingPayment, request.Total, request.ItemCount,
                time.GetUtcNow().AddSeconds(request.PaymentDueInSeconds), customer);
            store.Save(order);

            return Results.Ok(await rules.RunAsync(new RuleEvent("OrderPlaced", order), cancellationToken));
        });

        orders.MapPost("/{number}/pay", (string number, OrderStore store, RuleRunner rules, CancellationToken cancellationToken) =>
            UpdateAsync(number, OrderStatus.Paid, store, rules, cancellationToken));

        orders.MapPost("/{number}/ship", (string number, OrderStore store, RuleRunner rules, CancellationToken cancellationToken) =>
            UpdateAsync(number, OrderStatus.Shipped, store, rules, cancellationToken));
    }

    private static async Task<IResult> UpdateAsync(string number, OrderStatus status, OrderStore store, RuleRunner rules,
        CancellationToken cancellationToken)
    {
        if (store.Find(number) is not { } before)
            return Results.NotFound(new { error = $"No order '{number}'." });

        var after = before with { Status = status };
        store.Save(after);

        return Results.Ok(await rules.RunAsync(new RuleEvent("OrderUpdated", after, before), cancellationToken));
    }
}
