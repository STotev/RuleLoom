using System.Text.Json;
using System.Text.Json.Nodes;

namespace RuleLoom.Schemas.Tests;

public class SubjectTests
{
    [Fact]
    public void Build_Subject_IsWrittenWithItsIdentityAndLoader()
    {
        var schema = new LoadedSubjectSchema().Build();
        var subject = JsonNode.Parse(schema.ToJson())!["x-ruleloom-subject"]!;

        Assert.Equal("number", (string?)subject["x-ruleloom-identity"]);
        Assert.Equal("order", (string?)subject["x-ruleloom-provider"]);
        Assert.Equal(new SubjectDefinition(new ContextReference("number", new Dictionary<string, string>()), "order"), schema.Subject);
    }

    [Fact]
    public void Build_CompositeSubject_KeepsItsPartsInOrder_AndNeedsNoLoader()
    {
        var schema = new CompositeSubjectSchema().Build();

        Assert.NotNull(schema.Subject);
        Assert.True(schema.Subject.Identity.IsComposite);
        Assert.Equal(["country", "postcode"], schema.Subject.Identity.Parts.Keys);
        Assert.Equal("address.country", schema.Subject.Identity.Parts["country"]);
        Assert.Null(schema.Subject.Provider);
    }

    [Fact]
    public void Build_WithoutASubject_HasNone()
    {
        var schema = new OrderPlacedSchema().Build();

        Assert.Null(schema.Subject);
        Assert.DoesNotContain("x-ruleloom-subject", schema.ToJson());
    }

    [Fact]
    public void ToJson_Parse_RoundTripsTheSubject()
    {
        var schema = new CompositeSubjectSchema().Build();

        var parsed = EventSchemaDefinition.Parse(schema.ToJson());

        Assert.Equal(schema.Subject, parsed.Subject);
        Assert.Equal(["country", "postcode"], parsed.Subject!.Identity.Parts.Keys);
    }

    [Fact]
    public void Subject_Twice_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new TwiceSubjectSchema());
    }

    [Theory]
    [InlineData("""{ "x-ruleloom-event": "E", "x-ruleloom-subject": 1 }""", "'x-ruleloom-subject' must be an object.")]
    [InlineData("""{ "x-ruleloom-event": "E", "x-ruleloom-subject": {} }""", "The subject needs an identity ('x-ruleloom-identity').")]
    [InlineData("""{ "x-ruleloom-event": "E", "x-ruleloom-subject": { "x-ruleloom-identity": 1 } }""", "The subject's identity must be a path or an object of paths.")]
    [InlineData("""{ "x-ruleloom-event": "E", "x-ruleloom-subject": { "x-ruleloom-identity": "id", "x-ruleloom-provider": "" } }""", "The subject's provider must be a non-empty string.")]
    public void Parse_InvalidSubject_Throws(string json, string message)
    {
        var ex = Assert.Throws<JsonException>(() => EventSchemaDefinition.Parse(json));

        Assert.Equal(message, ex.Message);
    }

    private sealed class LoadedSubjectSchema : EventSchema<OrderDto>
    {
        public LoadedSubjectSchema()
        {
            Event("OrderPlaced");
            Subject(o => o.Number).LoadedBy("order");
        }
    }

    private sealed class CompositeSubjectSchema : EventSchema<OrderDto>
    {
        public CompositeSubjectSchema()
        {
            Event("AddressChanged");
            Subject(o => new { o.Address.Country, o.Address.Postcode });
        }
    }

    private sealed class TwiceSubjectSchema : EventSchema<OrderDto>
    {
        public TwiceSubjectSchema()
        {
            Event("OrderPlaced");
            Subject(o => o.Number);
            Subject(o => o.Id);
        }
    }
}
