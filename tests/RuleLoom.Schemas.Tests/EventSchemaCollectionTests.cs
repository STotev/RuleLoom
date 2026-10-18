namespace RuleLoom.Schemas.Tests;

public class EventSchemaCollectionTests
{
    [Fact]
    public void AddFromAssemblyOf_AddsPublicSchemaClassesOrderedByName()
    {
        var schemas = new EventSchemaCollection().AddFromAssemblyOf<OrderPlacedSchema>().Build();

        Assert.Equal(["LineAdded", "OrderPlaced"], schemas.Select(s => s.EventType));
    }

    [Fact]
    public void Build_SameEventTwice_Throws()
    {
        var collection = new EventSchemaCollection().Add<LineAddedSchema>().Add(new LineAddedSchema());

        var error = Assert.Throws<InvalidOperationException>(() => collection.Build());

        Assert.Contains("LineAdded", error.Message);
    }

    [Theory]
    [InlineData("customerTier", "Customer tier")]
    [InlineData("OrderPlaced", "Order placed")]
    [InlineData("vatID", "Vat ID")]
    [InlineData("VATNumber", "VAT number")]
    [InlineData("id", "Id")]
    [InlineData("customer", "Customer")]
    [InlineData("order_total", "Order total")]
    [InlineData("line2Sku", "Line2 sku")]
    public void Labels_FromNames(string name, string label)
    {
        Assert.Equal(label, Labels.FromName(name));
    }
}
