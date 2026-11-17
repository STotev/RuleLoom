using System.Text.Json.Serialization;
using RuleLoom.Schemas;

namespace RuleLoom.Samples.AspNetCore.Orders;

// The shop's orders, and the events it raises about them: what rules on orders can use.

public sealed record Customer(string Name, string Email);

public sealed record Order(string Number, OrderStatus Status, decimal Total, int ItemCount, DateTimeOffset PaymentDueAt, Customer Customer);

[JsonConverter(typeof(JsonStringEnumConverter<OrderStatus>))]   // by name, in JSON and in the catalog
public enum OrderStatus
{
    AwaitingPayment,
    Paid,
    Shipped
}

/// <summary>"OrderPlaced": raised when a customer places an order.</summary>
public sealed class OrderPlacedSchema : EventSchema<Order>
{
    public OrderPlacedSchema()
    {
        Event("OrderPlaced").Label("Order placed");
        Subject(o => o.Number).LoadedBy("order");   // scheduled actions are checked against the order as it is when due
        Context("customer").ReferencedBy(o => o.Customer.Email).Label("Customer");
        Context("orderHistory").ReferencedBy(o => o.Customer.Email).Label("Earlier orders");
    }
}

/// <summary>"OrderUpdated": raised when an order is paid or shipped, with the order before and after.</summary>
public sealed class OrderUpdatedSchema : EventSchema<Order>
{
    public OrderUpdatedSchema()
    {
        Event("OrderUpdated").Label("Order updated");
        Subject(o => o.Number).LoadedBy("order");
        Context("customer").ReferencedBy(o => o.Customer.Email).Label("Customer");
    }
}
