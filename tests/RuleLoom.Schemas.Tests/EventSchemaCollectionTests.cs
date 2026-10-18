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
}
