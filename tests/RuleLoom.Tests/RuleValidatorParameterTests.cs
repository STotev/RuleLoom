using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Context;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Schemas;
using RuleLoom.Validation;
using static RuleLoom.Tests.Conditions;

namespace RuleLoom.Tests;

public class RuleValidatorParameterTests
{
    private static readonly RuleValidator Validator = new(new CatalogBuilder()
        .AddEvent<ShipmentSchema>()
        .AddContextProvider<CustomerProvider>("customer")
        .AddAction<NotifyAction>("notify")
        .AddAction<LogAction>("log")
        .Build());

    [Fact]
    public void Validate_ParametersOfTheirTypes_AreValid()
    {
        var rule = RuleJsonParser.Parse("""
            { "id": "r", "name": "R", "eventType": "Shipped", "condition": { "operator": "eq", "operands": [ { "path": "Count" }, { "value": 1 } ] },
              "actions": [ { "action": "notify", "parameters": {
                "TO": { "$path": "@customer.Email" }, "attempts": 3, "channel": "sms", "amount": 5, "sendAt": "2026-05-01T14:00:00Z",
                "dueOn": "2026-05-02", "urgent": true, "cc": { "email": "ops@example.com" }, "tags": [ "a", "b" ], "extra": { "any": [ 1 ] } } },
                { "action": "notify", "parameters": {
                "to": { "$path": "Email" }, "attempts": { "$path": "@customer.Age" }, "amount": { "$path": "Weight" },
                "sendAt": { "$path": "ShippedAt" }, "tags": { "$path": "Labels" }, "cc": null } } ] }
            """);

        var result = Validator.Validate(rule);

        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Validate_UnknownParameter_IsAnError()
    {
        var result = Validator.Validate(Rule(Notify(("to", "ada@example.com"), ("attempts", 1), ("subjet", "Hi"))));

        Assert.Equal(new RuleValidationError("$.actions[0].parameters.subjet", "Action 'notify' has no parameter 'subjet'."),
            Assert.Single(result.Errors));
    }

    [Fact]
    public void Validate_MissingRequiredParameter_IsAnError()
    {
        var result = Validator.Validate(Rule(Notify(("to", "ada@example.com"))));

        Assert.Equal(new RuleValidationError("$.actions[0].parameters", "Action 'notify' requires the parameter 'attempts'."),
            Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData("attempts", "3", "'attempts' must be a whole number, not the text '3'.")]
    [InlineData("attempts", 2.5, "'attempts' must be a whole number, not 2.5.")]
    [InlineData("amount", true, "'amount' must be a number, not true.")]
    [InlineData("to", 5, "'to' must be text, not 5.")]
    [InlineData("to", null, "'to' can't be null.")]
    [InlineData("channel", "Fax", "'channel' must be one of Email, Sms, not the text 'Fax'.")]
    [InlineData("channel", 1, "'channel' must be one of Email, Sms, not 1.")]
    [InlineData("sendAt", "tomorrow", "'sendAt' must be a date and time (ISO 8601), not the text 'tomorrow'.")]
    [InlineData("dueOn", "2026-13-01", "'dueOn' must be a date (yyyy-MM-dd), not the text '2026-13-01'.")]
    [InlineData("urgent", "yes", "'urgent' must be a boolean, not the text 'yes'.")]
    [InlineData("tags", "a", "'tags' must be a list, not the text 'a'.")]
    [InlineData("cc", "ops@example.com", "'cc' must be an object, not the text 'ops@example.com'.")]
    public void Validate_ValueOfTheWrongType_IsAnErrorAtTheParameter(string name, object? value, string message)
    {
        var result = Validator.Validate(Rule(Notify(("to", "ada@example.com"), ("attempts", 1), (name, value))));

        Assert.Equal(new RuleValidationError($"$.actions[0].parameters.{name}", message), Assert.Single(result.Errors));
    }

    [Fact]
    public void Validate_ObjectsAndLists_AreCheckedInside()
    {
        var cc = new Dictionary<string, object?> { ["mail"] = "ops@example.com", ["name"] = 1 };
        var tags = new List<object?> { "a", 2 };

        var result = Validator.Validate(Rule(Notify(("to", "ada@example.com"), ("attempts", 1), ("cc", cc), ("tags", tags))));

        Assert.Equal(
        [
            new RuleValidationError("$.actions[0].parameters.cc.mail", "'cc' has no field 'mail'."),
            new RuleValidationError("$.actions[0].parameters.cc", "'cc' requires the field 'email'."),
            new RuleValidationError("$.actions[0].parameters.cc.name", "'cc.name' must be text, not 1."),
            new RuleValidationError("$.actions[0].parameters.tags[1]", "'tags[1]' must be text, not 2.")
        ], result.Errors);
    }

    [Fact]
    public void Validate_ParameterPaths_AreCheckedLikeConditionPaths()
    {
        var result = Validator.Validate(Rule(Notify(("to", RulePath.Parse("@customer.Emial")), ("attempts", RulePath.Parse("Labels.Count")))));

        Assert.Equal(
        [
            new RuleValidationError("$.actions[0].parameters.to.$path", "'Emial' is not a field of '@customer'."),
            new RuleValidationError("$.actions[0].parameters.attempts.$path", "'Labels' is a list; paths can't go into its items ('Labels.Count').")
        ], result.Errors);
    }

    [Fact]
    public void Validate_PathWhoseFieldCantBeReadAsTheParameter_IsAWarning()
    {
        var result = Validator.Validate(Rule(Notify(("to", RulePath.Parse("Fragile")), ("attempts", RulePath.Parse("@customer.Email")))));

        Assert.True(result.IsValid);
        Assert.Equal(
        [
            new RuleValidationError("$.actions[0].parameters.to.$path", "'to' is text, but 'Fragile' is a boolean; the action can't read it."),
            new RuleValidationError("$.actions[0].parameters.attempts.$path", "'attempts' is a number, but '@customer.Email' is text; the action can't read it.")
        ], result.Warnings);
    }

    [Fact]
    public void Validate_UntypedAction_ParametersArentChecked_ButTheirPathsAre()
    {
        var log = new ActionDefinition { Action = "log", Parameters = { ["anything"] = 1, ["message"] = RulePath.Parse("Weigth") } };

        var result = Validator.Validate(Rule(log));

        Assert.Equal(new RuleValidationError("$.actions[0].parameters.message.$path", "'Weigth' is not a field of Shipped."),
            Assert.Single(result.Errors));
    }

    private static RuleDefinition Rule(params ActionDefinition[] actions) =>
        new() { Id = "rule", Name = "Rule", EventType = "Shipped", Condition = Op("eq", Path("Count"), Value(1)), Actions = [.. actions] };

    // Later parameters replace earlier ones of the same name.
    private static ActionDefinition Notify(params (string Name, object? Value)[] parameters)
    {
        var action = new ActionDefinition { Action = "notify" };
        foreach (var (name, value) in parameters)
            action.Parameters[name] = value;
        return action;
    }

    public enum Channel
    {
        Email,
        Sms
    }

    public sealed record Recipient(string Email, string? Name = null);

    public sealed record NotifyParameters(
        string To,
        int Attempts,
        Channel Channel = Channel.Email,
        decimal? Amount = null,
        DateTimeOffset? SendAt = null,
        DateOnly? DueOn = null,
        bool Urgent = false,
        Recipient? Cc = null,
        IReadOnlyList<string>? Tags = null,
        object? Extra = null);

    public sealed class ShipmentDto
    {
        public string Email { get; set; } = "";
        public int Count { get; set; }
        public decimal Weight { get; set; }
        public bool Fragile { get; set; }
        public DateTimeOffset ShippedAt { get; set; }
        public List<string> Labels { get; set; } = [];
        public string CustomerId { get; set; } = "";
    }

    public sealed class CustomerDto
    {
        public string Email { get; set; } = "";
        public int Age { get; set; }
    }

    public sealed class ShipmentSchema : EventSchema<ShipmentDto>
    {
        public ShipmentSchema()
        {
            Event("Shipped");
            Context("customer").ReferencedBy(s => s.CustomerId);
        }
    }

    public sealed class CustomerProvider : ContextProvider<CustomerDto>
    {
        protected override ValueTask<CustomerDto?> LoadAsync(ContextRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<CustomerDto?>(null);
    }

    public sealed class NotifyAction : IAction<NotifyParameters>
    {
        public Task<ActionResult> ExecuteAsync(NotifyParameters parameters, ActionDefinition action, EvaluationContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(ActionResult.Success());
    }

    public sealed class LogAction : IAction
    {
        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActionResult.Success());
    }
}
