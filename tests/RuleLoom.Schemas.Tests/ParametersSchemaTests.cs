using System.Text.Json;
using System.Text.Json.Nodes;

namespace RuleLoom.Schemas.Tests;

public class ParametersSchemaTests
{
    [Fact]
    public void Build_DescribesTheTypeWithRequiredParametersFromIt()
    {
        var schema = new ParametersSchema<EmailParameters>().Build();
        var document = JsonNode.Parse(schema.ToJson())!;

        Assert.Equal(["to", "subject", "cc", "priority"], schema.Fields.Select(f => f.Name));
        Assert.Equal(["to", "subject"], document["required"]!.AsArray().Select(n => (string?)n));
        Assert.Equal([true, true, false, false], schema.Fields.Select(f => f.IsRequired));
        Assert.Equal(FieldType.Enum, schema.Fields[3].Type);
        Assert.Equal(["Low", "High"], schema.Fields[3].EnumValues);
        Assert.True(schema.Fields[2].IsNullable);
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", (string?)document["$schema"]);
    }

    [Fact]
    public void Build_RequiredMembers_AreRequired_AndNumbersAreNotText()
    {
        var document = JsonNode.Parse(new ParametersSchema<ReminderParameters>().Build().ToJson())!;

        Assert.Equal(["message"], document["required"]!.AsArray().Select(n => (string?)n));
        Assert.Equal("integer", (string?)document["properties"]!["attempts"]!["type"]);
    }

    [Fact]
    public void Parameter_AddsLabelsAndIgnores()
    {
        var parameters = new ParametersSchema<EmailParameters>();
        parameters.Parameter(p => p.To).Label("Recipient").Description("Who gets the email.");
        parameters.Parameter(p => p.Cc).Ignore();

        var schema = parameters.Build();

        Assert.Equal(["to", "subject", "priority"], schema.Fields.Select(f => f.Name));
        Assert.Equal(("Recipient", "Who gets the email."), (schema.Fields[0].Label, schema.Fields[0].Description));
    }

    [Fact]
    public void Build_NotAnObject_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new ParametersSchema<int>().Build());
    }

    [Fact]
    public void ToJson_Parse_RoundTrips()
    {
        var parameters = new ParametersSchema<EmailParameters>();
        parameters.Parameter(p => p.To).Label("Recipient");
        var schema = parameters.Build();

        var parsed = ParametersSchemaDefinition.Parse(schema.ToJson());

        Assert.Equal(schema.Fields, parsed.Fields);
        Assert.Equal(schema.ToJson(), parsed.ToJson());
    }

    [Fact]
    public void Parse_NotAnObject_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => ParametersSchemaDefinition.Parse("[]"));
    }

    public enum Priority
    {
        Low,
        High
    }

    public sealed record EmailParameters(string To, string Subject, string? Cc = null, Priority Priority = Priority.Low);

    public sealed class ReminderParameters
    {
        public required string Message { get; init; }
        public int Attempts { get; init; }
    }
}
