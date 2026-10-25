namespace RuleLoom.Schemas.Tests;

public class SchemaCollectionTests
{
    [Fact]
    public void AddFromAssemblyOf_AddsPublicSchemaClassesOrderedByName()
    {
        var schemas = new SchemaCollection().AddFromAssemblyOf<OrderPlacedSchema>().Build().Events;

        Assert.Equal(["LineAdded", "OrderPlaced"], schemas.Select(s => s.EventType));
    }

    [Fact]
    public void Build_SameEventTwice_Throws()
    {
        var collection = new SchemaCollection().Add<LineAddedSchema>().Add(new LineAddedSchema());

        var error = Assert.Throws<InvalidOperationException>(() => collection.Build());

        Assert.Contains("LineAdded", error.Message);
    }

    [Fact]
    public void Build_HoldsOnlyEvents()
    {
        var document = new SchemaCollection().Add<OrderPlacedSchema>().Build();

        Assert.Equal(["OrderPlaced"], document.Events.Select(e => e.EventType));
        Assert.Empty(document.Contexts);
        Assert.Empty(document.Operators);
        Assert.Empty(document.Actions);
    }
}
