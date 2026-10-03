using System.Text;
using System.Text.Json;
using RuleForge.Abstractions.Condition;
using RuleForge.Abstractions.Rule;

namespace RuleForge.Tests;

public class RuleJsonValidationTests
{
    private const string ValidCondition = """{ "operator": "eq", "operands": [{ "path": "Price" }, { "value": 1 }] }""";

    public static TheoryData<string, string> InvalidRules => new()
    {
        // Rule shape and required properties
        { "[]", "$" },
        { """{ "eventType": "E", "condition": {{C}} }""", "$.name" },
        { """{ "name": "", "eventType": "E", "condition": {{C}} }""", "$.name" },
        { """{ "name": "   ", "eventType": "E", "condition": {{C}} }""", "$.name" },
        { """{ "name": 5, "eventType": "E", "condition": {{C}} }""", "$.name" },
        { """{ "name": "R", "condition": {{C}} }""", "$.eventType" },
        { """{ "name": "R", "eventType": null, "condition": {{C}} }""", "$.eventType" },
        { """{ "name": "R", "eventType": "E" }""", "$.condition" },
        { """{ "name": "R", "eventType": "E", "condition": null }""", "$.condition" },
        // Root condition must be an operator
        { """{ "name": "R", "eventType": "E", "condition": { "path": "Price" } }""", "$.condition" },
        { """{ "name": "R", "eventType": "E", "condition": { "value": true } }""", "$.condition" },
        // Condition nodes
        { """{ "name": "R", "eventType": "E", "condition": { "operator": "" } }""", "$.condition.operator" },
        { """{ "name": "R", "eventType": "E", "condition": { "operator": 1 } }""", "$.condition.operator" },
        { """{ "name": "R", "eventType": "E", "condition": { "operator": "eq", "path": "Price" } }""", "$.condition" },
        { """{ "name": "R", "eventType": "E", "condition": { "operator": "eq", "operands": {} } }""", "$.condition.operands" },
        { """{ "name": "R", "eventType": "E", "condition": { "operator": "eq", "operands": [{ "path": "Price" }, {}] } }""", "$.condition.operands[1]" },
        { """{ "name": "R", "eventType": "E", "condition": { "operator": "eq", "operands": [{ "path": "" }] } }""", "$.condition.operands[0].path" },
        { """{ "name": "R", "eventType": "E", "condition": { "operator": "eq", "operands": [{ "path": "A", "value": 1 }] } }""", "$.condition.operands[0]" },
        { """{ "name": "R", "eventType": "E", "condition": { "operator": "eq", "operands": ["Price"] } }""", "$.condition.operands[0]" },
        { """{ "name": "R", "eventType": "E", "condition": { "operator": "and", "operands": [{ "operator": "not", "operands": [{ "operator": " " }] }] } }""", "$.condition.operands[0].operands[0].operator" },
        // Actions
        { """{ "name": "R", "eventType": "E", "condition": {{C}}, "actions": {} }""", "$.actions" },
        { """{ "name": "R", "eventType": "E", "condition": {{C}}, "actions": [5] }""", "$.actions[0]" },
        { """{ "name": "R", "eventType": "E", "condition": {{C}}, "actions": [{ "parameters": {} }] }""", "$.actions[0].type" },
        { """{ "name": "R", "eventType": "E", "condition": {{C}}, "actions": [{ "type": "log" }, { "type": "" }] }""", "$.actions[1].type" },
        { """{ "name": "R", "eventType": "E", "condition": {{C}}, "actions": [{ "type": "log", "parameters": [] }] }""", "$.actions[0].parameters" },
        { """{ "name": "R", "eventType": "E", "condition": {{C}}, "actions": [{ "type": "log", "condition": { "path": "Active" } }] }""", "$.actions[0].condition" }
    };

    [Theory]
    [MemberData(nameof(InvalidRules))]
    public void Parse_InvalidRule_ThrowsWithLocation(string json, string expectedPath)
    {
        var ex = Assert.Throws<RuleParseException>(() => RuleJsonParser.Parse(Expand(json)));

        Assert.Equal(expectedPath, ex.Path);
        Assert.Contains($"(at {expectedPath})", ex.Message);
    }

    [Fact]
    public void Parse_RuleParseException_IsJsonException()
    {
        Assert.ThrowsAny<JsonException>(() => RuleJsonParser.Parse("""{ "name": "R", "eventType": "E" }"""));
    }

    [Fact]
    public void Parse_MalformedJson_ThrowsJsonException()
    {
        var ex = Assert.Throws<JsonException>(() => RuleJsonParser.Parse("{ \"name\": "));

        Assert.IsNotType<RuleParseException>(ex);
    }

    [Fact]
    public void Parse_ValidRuleWithOptionalPartsOmittedOrNull_Succeeds()
    {
        var rule = RuleJsonParser.Parse(Expand(
            """{ "name": "R", "eventType": "E", "condition": {{C}}, "actions": [{ "type": "log", "condition": null }] }"""));

        Assert.Null(Assert.Single(rule.Actions).Condition);
    }

    [Fact]
    public void Parse_OperatorWithoutOperands_IsStructurallyValid()
    {
        var rule = RuleJsonParser.Parse("""{ "name": "R", "eventType": "E", "condition": { "operator": "always" } }""");

        Assert.Empty(Assert.IsType<OperatorCondition>(rule.Condition).Operands);
    }

    [Fact]
    public void Parse_ExplicitNullValue_IsValid()
    {
        var rule = RuleJsonParser.Parse(
            """{ "name": "R", "eventType": "E", "condition": { "operator": "eq", "operands": [{ "path": "A" }, { "value": null }] } }""");

        var condition = Assert.IsType<OperatorCondition>(rule.Condition);
        Assert.Null(Assert.IsType<ValueOperand>(condition.Operands[1]).Value);
    }

    [Fact]
    public void ParseMany_InvalidRule_ReportsItsIndex()
    {
        var json = Expand(
            """
            [
              { "name": "R1", "eventType": "E", "condition": {{C}} },
              { "name": "R2", "eventType": "E", "condition": {{C}} },
              { "name": "R3", "eventType": "E" }
            ]
            """);

        var ex = Assert.Throws<RuleParseException>(() => RuleJsonParser.ParseMany(json));

        Assert.Equal("$[2].condition", ex.Path);
    }

    [Fact]
    public void ParseMany_NotAnArray_Throws()
    {
        var ex = Assert.Throws<RuleParseException>(
            () => RuleJsonParser.ParseMany(Expand("""{ "name": "R", "eventType": "E", "condition": {{C}} }""")));

        Assert.Equal("$", ex.Path);
    }

    [Fact]
    public async Task ParseAsync_InvalidRule_ThrowsWithLocation()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""{ "name": "R", "condition": {} }"""));

        var ex = await Assert.ThrowsAsync<RuleParseException>(() => RuleJsonParser.ParseAsync(stream));

        Assert.Equal("$.eventType", ex.Path);
    }

    private static string Expand(string json) => json.Replace("{{C}}", ValidCondition);
}
