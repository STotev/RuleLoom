using System.Text.Json.Serialization;
using RuleLoom.Schemas;

namespace RuleLoom.Samples.Console;

// The web shop every scenario runs against: its orders and customers, and the events it raises about orders.

public sealed record Customer(string Name, string Email);

public sealed record Order(string Number, OrderStatus Status, decimal Total, Customer Customer, DateTimeOffset? PaymentDueAt = null);

[JsonConverter(typeof(JsonStringEnumConverter<OrderStatus>))]   // written by name, so the catalog lists the values
public enum OrderStatus
{
    AwaitingPayment,
    Paid,
    Shipped
}

/// <summary>"OrderPlaced": raised when a customer places an order. Rules on it can use the order's fields.</summary>
public sealed class OrderPlacedSchema : EventSchema<Order>
{
    public OrderPlacedSchema() => Event("OrderPlaced").Label("Order placed");
}

/// <summary>"OrderUpdated": raised when an order changes, with the order before and after.</summary>
public sealed class OrderUpdatedSchema : EventSchema<Order>
{
    public OrderUpdatedSchema() => Event("OrderUpdated").Label("Order updated");
}

public static class Customers
{
    public static readonly Customer Ada = new("Ada", "ada@example.com");
    public static readonly Customer Bob = new("Bob", "bob@example.com");
}
