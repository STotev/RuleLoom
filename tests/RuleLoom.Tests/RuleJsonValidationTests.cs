using System.Text;
using System.Text.Json;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Rule;

namespace RuleLoom.Tests;

public class RuleJsonValidationTests
{
    private const string ValidCondition = """{ "operator": "eq", "operands": [{ "path": "Price" }, { "value": 1 }] }""";

    public static TheoryData<string, string> InvalidRules => new()
    {
        // Rule shape and required properties
        { "[]", "$" },
        { """{ "name": "R", "eventType": "E", "condition": {{C}} }""", "$.id" },
        { """{ "id": null, "name": "R", "eventType": "E", "condition": {{C}} }""", "$.id" },
        { """{ "id": "", "name": "R", "eventType": "E", "condition": {{C}} }""", "$.id" },
        { """{ "id": "  ", "name": "R", "eventType": "E", "condition": {{C}} }""", "$.id" },
        { """{ "id": 42, "name": "R", "eventType": "E", "condition": {{C}} }""", "$.id" },
        { """{ "id": "r", "eventType": "E", "condition": {{C}} }""", "$.name" },
        { """{ "id": "r", "name": "", "eventType": "E", "condition": {{C}} }""", "$.name" },
        { """{ "id": "r", "name": "   ", "eventType": "E", "condition": {{C}} }""", "$.name" },
        { """{ "id": "r", "name": 5, "eventType": "E", "condition": {{C}} }""", "$.name" },
        { """{ "id": "r", "name": "R", "condition": {{C}} }""", "$.eventType" },
        { """{ "id": "r", "name": "R", "eventType": null, "condition": {{C}} }""", "$.eventType" },
        { """{ "id": "r", "name": "R", "eventType": "E" }""", "$.condition" },
        { """{ "id": "r", "name": "R", "eventType": "E", "condition": null }""", "$.condition" },
        // Root condition must be an operator
        { """{ "id": "r", "name": "R", "eventType": "E", "condition": { "path": "Price" } }""", "$.condition" },
        { """{ "id": "r", "name": "R", "eventType": "E", "condition": { "value": true } }""", "$.condition" },
        // Condition nodes
        { """{ "id": "r", "name": "R", "eventType": "E", "condition": { "operator": "" } }""", "$.condition.operator" },
        { """{ "id": "r", "name": "R", "eventType": "E", "condition": { "operator": 1 } }""", "$.condition.operator" },
        { """{ "id": "r", "name": "R", "eventType": "E", "condition": { "operator": "eq", "path": "Price" } }""", "$.condition" },
        { """{ "id": "r", "name": "R", "eventType": "E", "condition": { "operator": "eq", "operands": {} } }""", "$.condition.operands" },
        { """{ "id": "r", "name": "R", "eventType": "E", "condition": { "operator": "eq", "operands": [{ "path": "Price" }, {}] } }""", "$.condition.operands[1]" },
        { """{ "id": "r", "name": "R", "eventType": "E", "condition": { "operator": "eq", "operands": [{ "path": "" }] } }""", "$.condition.operands[0].path" },
        { """{ "id": "r", "name": "R", "eventType": "E", "condition": { "operator": "eq", "operands": [{ "path": "A", "value": 1 }] } }""", "$.condition.operands[0]" },
        { """{ "id": "r", "name": "R", "eventType": "E", "condition": { "operator": "eq", "operands": ["Price"] } }""", "$.condition.operands[0]" },
        { """{ "id": "r", "name": "R", "eventType": "E", "condition": { "operator": "and", "operands": [{ "operator": "not", "operands": [{ "operator": " " }] }] } }""", "$.condition.operands[0].operands[0].operator" },
        // Actions
        { """{ "id": "r", "name": "R", "eventType": "E", "condition": {{C}}, "actions": {} }""", "$.actions" },
        { """{ "id": "r", "name": "R", "eventType": "E", "condition": {{C}}, "actions": [5] }""", "$.actions[0]" },
        { """{ "id": "r", "name": "R", "eventType": "E", "condition": {{C}}, "actions": [{ "parameters": {} }] }""", "$.actions[0].action" },
        { """{ "id": "r", "name": "R", "eventType": "E", "condition": {{C}}, "actions": [{ "action": "log" }, { "action": "" }] }""", "$.actions[1].action" },
        { """{ "id": "r", "name": "R", "eventType": "E", "condition": {{C}}, "actions": [{ "action": "log", "parameters": [] }] }""", "$.actions[0].parameters" },
        { """{ "id": "r", "name": "R", "eventType": "E", "condition": {{C}}, "actions": [{ "action": "log", "condition": { "path": "Active" } }] }""", "$.actions[0].condition" },
        // Path variables: only $current and $previous
        { """{ "id": "r", "name": "R", "eventType": "E", "condition": { "operator": "eq", "operands": [{ "path": "$now" }, { "value": 1 }] } }""", "$.condition.operands[0].path" },
        { """{ "id": "r", "name": "R", "eventType": "E", "condition": { "operator": "eq", "operands": [{ "path": "$cur.Price" }, { "value": 1 }] } }""", "$.condition.operands[0].path" },
        { """{ "id": "r", "name": "R", "eventType": "E", "condition": {{C}}, "actions": [{ "action": "log", "schedule": { "mode": "Delay", "amount": 1, "unit": "Hours", "referencePath": "$due" } }] }""", "$.actions[0].schedule.referencePath" }
    };

    [Theory]
    [MemberData(nameof(InvalidRules))]
    public void Parse_InvalidRule_ThrowsWithLocation(string json, string expectedPath)
    {
        var ex = Assert.Throws<RuleParseException>(() => RuleJsonParser.Parse(Expand(json)));

        Assert.Equal(expectedPath, ex.Path);
        Assert.Contains($"(at {expectedPath})", ex.Message);
    }

    [Theory]
    [InlineData("$current.Price")]
    [InlineData("$previous.Price")]
    [InlineData("$Current.Price")]
    [InlineData("$current")]
    [InlineData("current")]
    [InlineData("current.Price")]
    [InlineData("Previous.Order.Status")]
    [InlineData("Currently.Price")]
    public void Parse_ValidPaths_AreAccepted(string path)
    {
        var json = $$"""{ "id": "r", "name": "R", "eventType": "E", "condition": { "operator": "eq", "operands": [{ "path": "{{path}}" }, { "value": 1 }] } }""";

        var rule = RuleJsonParser.Parse(json);

        Assert.Equal(path, Assert.IsType<PathOperand>(((OperatorCondition)rule.Condition).Operands[0]).Path);
    }

    [Fact]
    public void Parse_RuleParseException_IsJsonException()
    {
        Assert.ThrowsAny<JsonException>(() => RuleJsonParser.Parse("""{ "id": "r", "name": "R", "eventType": "E" }"""));
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
            """{ "id": "r", "name": "R", "eventType": "E", "condition": {{C}}, "actions": [{ "action": "log", "condition": null }] }"""));

        Assert.Null(Assert.Single(rule.Actions).Condition);
    }

    [Fact]
    public void Parse_OperatorWithoutOperands_IsStructurallyValid()
    {
        var rule = RuleJsonParser.Parse("""{ "id": "r", "name": "R", "eventType": "E", "condition": { "operator": "always" } }""");

        Assert.Empty(Assert.IsType<OperatorCondition>(rule.Condition).Operands);
    }

    [Fact]
    public void Parse_ExplicitNullValue_IsValid()
    {
        var rule = RuleJsonParser.Parse(
            """{ "id": "r", "name": "R", "eventType": "E", "condition": { "operator": "eq", "operands": [{ "path": "A" }, { "value": null }] } }""");

        var condition = Assert.IsType<OperatorCondition>(rule.Condition);
        Assert.Null(Assert.IsType<ValueOperand>(condition.Operands[1]).Value);
    }

    [Fact]
    public void ParseMany_InvalidRule_ReportsItsIndex()
    {
        var json = Expand(
            """
            [
              { "id": "r1", "name": "R1", "eventType": "E", "condition": {{C}} },
              { "id": "r2", "name": "R2", "eventType": "E", "condition": {{C}} },
              { "id": "r3", "name": "R3", "eventType": "E" }
            ]
            """);

        var ex = Assert.Throws<RuleParseException>(() => RuleJsonParser.ParseMany(json));

        Assert.Equal("$[2].condition", ex.Path);
    }

    [Fact]
    public void ParseMany_RuleWithoutId_ReportsItsIndex()
    {
        var json = Expand(
            """
            [
              { "id": "r1", "name": "R1", "eventType": "E", "condition": {{C}} },
              { "name": "R2", "eventType": "E", "condition": {{C}} }
            ]
            """);

        var ex = Assert.Throws<RuleParseException>(() => RuleJsonParser.ParseMany(json));

        Assert.Equal("$[1].id", ex.Path);
    }

    [Fact]
    public void ParseMany_NotAnArray_Throws()
    {
        var ex = Assert.Throws<RuleParseException>(
            () => RuleJsonParser.ParseMany(Expand("""{ "id": "r", "name": "R", "eventType": "E", "condition": {{C}} }""")));

        Assert.Equal("$", ex.Path);
    }

    [Fact]
    public async Task ParseAsync_InvalidRule_ThrowsWithLocation()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""{ "id": "r", "name": "R", "condition": {} }"""));

        var ex = await Assert.ThrowsAsync<RuleParseException>(() => RuleJsonParser.ParseAsync(stream));

        Assert.Equal("$.eventType", ex.Path);
    }

    private static string Expand(string json) => json.Replace("{{C}}", ValidCondition);
}
