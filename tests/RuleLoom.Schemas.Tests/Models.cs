using System.Text.Json.Serialization;

namespace RuleLoom.Schemas.Tests;

public enum OrderStatus { Placed, Paid }

public sealed class OrderDto
{
    public decimal Total { get; set; }
    public int Quantity { get; set; }
    public bool Paid { get; set; }
    public string Number { get; set; } = "";
    public string? Note { get; set; }
    public OrderStatus Status { get; set; }
    public DateTimeOffset PlacedAt { get; set; }
    public DateOnly? DueOn { get; set; }
    public Guid Id { get; set; }
    public string CustomerId { get; set; } = "";
    public AddressDto Address { get; set; } = new();
    public CustomerDto Customer { get; set; } = new();
    public List<LineDto> Lines { get; set; } = [];
    public string[] Tags { get; set; } = [];
    public Dictionary<string, string> Attributes { get; set; } = [];
    public string InternalNotes { get; set; } = "";
    [JsonIgnore] public string Secret { get; set; } = "";
}

public sealed class CustomerDto
{
    public string Tier { get; set; } = "";
    public string VatID { get; set; } = "";
    public string InternalId { get; set; } = "";
    public List<OrderDto> Orders { get; set; } = [];
}

public sealed class AddressDto
{
    public string Country { get; set; } = "";
    public string Postcode { get; set; } = "";
}

public sealed class LineDto
{
    public string Sku { get; set; } = "";
}

public sealed class UserDto
{
    public string Email { get; set; } = "";
    public UserDto? Manager { get; set; }
}

public sealed class OrderPlacedSchema : EventSchema<OrderDto>
{
    public OrderPlacedSchema()
    {
        Event("OrderPlaced").Label("Order placed").Description("A customer placed an order.");

        Field(o => o.Total).Label("Order total");
        Field(o => o.Customer.Tier).Description("Loyalty tier at the time of the order.");
        Field(o => o.InternalNotes).Ignore();

        Field(o => o.CustomerId).Ignore();

        Context("customer").ReferencedBy(o => o.CustomerId).Label("Customer").Description("The customer who placed the order.");
        Context("shipping-zone").ReferencedBy(o => new { o.Address.Country, o.Address.Postcode });
        Context("calendar");
    }
}

public sealed class LineAddedSchema : EventSchema<LineDto>
{
    public LineAddedSchema() => Event("LineAdded");
}
