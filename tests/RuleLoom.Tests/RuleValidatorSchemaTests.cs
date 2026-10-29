using System.Text.Json.Serialization;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Abstractions.Scheduling;
using RuleLoom.Operators;
using RuleLoom.Schemas;
using RuleLoom.Validation;
using static RuleLoom.Tests.Conditions;

namespace RuleLoom.Tests;

public class RuleValidatorSchemaTests
{
    private static readonly RuleValidator Validator = new(Catalog().Build());

    [Fact]
    public void Validate_EventNotInTheCatalog_IsAnErrorAndPathsArentChecked()
    {
        var rule = new RuleDefinition { Id = "rule", Name = "Rule", EventType = "Unknown", Condition = Op("eq", Path("Totl"), Value(1)) };

        var result = Validator.Validate(rule);

        Assert.Equal(new RuleValidationError("$.eventType", "Event type 'Unknown' isn't in the catalog."), Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData("Total")]
    [InlineData("total")]
    [InlineData("$current.Customer.Tier")]
    [InlineData("$previous.customer.tier")]
    [InlineData("$current")]
    [InlineData("Lines")]
    [InlineData("Attributes.anything")]   // a dictionary: no declared fields
    [InlineData("Payload.any.depth")]     // untyped
    [InlineData("Node.Next.Next.Name")]   // a repeated type below the first level
    [InlineData("@customer")]
    [InlineData("@customer.tier")]
    [InlineData("@billingAccount.balance")]
    [InlineData("@watchers")]
    public void Validate_KnownPaths_AreValid(string path)
    {
        var result = Validate(Rule(Op("eq", Path(path), Value(null))));

        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("Totl", "'Totl' is not a field of OrderPlaced.")]
    [InlineData("InternalNotes", "'InternalNotes' is not a field of OrderPlaced.")]
    [InlineData("$previous.Customer.Tierr", "'Tierr' is not a field of 'Customer'.")]
    [InlineData("Total.Amount", "'Total' is a number and has no fields ('Total.Amount').")]
    [InlineData("Lines.Sku", "'Lines' is a list; paths can't go into its items ('Lines.Sku').")]
    [InlineData("@account.balance", "'@account' is not a context of OrderPlaced.")]
    [InlineData("@unregistered.name", "No context provider is registered under 'unregistered' (context '@unregistered').")]
    [InlineData("@customer.missing", "'missing' is not a field of '@customer'.")]
    [InlineData("@watchers.tier", "'@watchers' is a collection; paths can't go into its items ('@watchers.tier').")]
    [InlineData("$previous.@customer.tier", "Contexts have no current or previous state; use '@customer.tier' instead of '$previous.@customer.tier'.")]
    [InlineData("$state.Total", "Unknown variable '$state' in path '$state.Total'. Use $current, $previous or $event.")]
    [InlineData("#now", "Paths starting with '#' are reserved for functions: '#now'.")]
    public void Validate_WrongPaths_AreErrorsAtTheirLocation(string path, string message)
    {
        var result = Validate(Rule(Op("and", Op("eq", Path("Paid"), Value(true)), Op("eq", Path(path), Value(null)))));

        Assert.False(result.IsValid);
        Assert.Equal(new RuleValidationError("$.condition.operands[1].operands[0].path", message), Assert.Single(result.Errors));
    }

    [Fact]
    public void Validate_ChecksActionConditionsAndScheduleReferencePaths()
    {
        var rule = Rule(Op("eq", Path("Paid"), Value(true)),
            Action(Op("eq", Path("Nope"), Value(1))),
            Action(schedule: new ScheduleMetadata { Mode = ScheduleMode.Advance, Amount = 1, Unit = ScheduleUnit.Days, ReferencePath = "DueOnn" }));

        var result = Validate(rule);

        Assert.Equal(["$.actions[0].condition.operands[0].path", "$.actions[1].schedule.referencePath"], result.Errors.Select(e => e.Path));
    }

    [Fact]
    public void Validate_KeepsTheOtherErrorsAndReportsEverything()
    {
        var rule = Rule(Op("and", Op("eq", Path("Totl"), Value(1)), Op("unknown")), new ActionDefinition { Action = "nope" });

        var result = Validate(rule);

        Assert.Equal(["$.condition.operands[0].operands[0].path", "$.condition.operands[1].operator", "$.actions[0].action"],
            result.Errors.Select(e => e.Path));   // document order
    }

    [Fact]
    public void Validate_ComparedPaths_AreReportedOnceInDocumentOrder()
    {
        var result = Validate(Rule(Op("eq", Path("#now"), Path("Unknown"))));

        Assert.Equal(["$.condition.operands[0].path", "$.condition.operands[1].path"], result.Errors.Select(e => e.Path));
    }

    [Fact]
    public void Validate_ContextWithoutSchema_IsAWarning()
    {
        var result = Validate(Rule(Op("eq", Path("@legacy.anything"), Value(1))));

        Assert.True(result.IsValid);
        Assert.Equal(new RuleValidationError("$.condition.operands[0].path", "Context '@legacy' has no schema; paths into it aren't checked."),
            Assert.Single(result.Warnings));
    }

    [Fact]
    public void Validate_ChangedOnAContext_IsAnError()
    {
        var result = Validate(Rule(Op("changed", Path("@customer.tier"))));

        Assert.Equal("$.condition.operands[0].path", Assert.Single(result.Errors).Path);
    }

    [Theory]
    [InlineData("eq", "Total", "100")]
    [InlineData("eq", "Paid", "yes")]
    [InlineData("eq", "Quantity", true)]
    [InlineData("gt", "PlacedAt", "yesterday")]
    [InlineData("eq", "Number", 5)]
    public void Validate_ValuesThatCantMatch_AreWarnings(string @operator, string path, object value)
    {
        var result = Validate(Rule(Op(@operator, Path(path), Value(value))));

        Assert.True(result.IsValid);
        Assert.Equal("$.condition", Assert.Single(result.Warnings).Path);
    }

    [Theory]
    [InlineData("eq", "Total", 100)]
    [InlineData("gte", "Total", 99.5)]
    [InlineData("eq", "Paid", true)]
    [InlineData("eq", "Number", "A-1")]
    [InlineData("lt", "PlacedAt", "2026-10-21T10:00:00Z")]
    [InlineData("eq", "Status", "paid")]
    [InlineData("eq", "Status", "Shipped")]   // enums aren't judged: CLR and JSON state match differently
    [InlineData("eq", "Status", 1)]
    [InlineData("gt", "Paid", false)]         // booleans are ordered
    [InlineData("eq", "Note", null)]
    [InlineData("eq", "Customer", "anything")]   // objects aren't checked
    public void Validate_ValuesThatCanMatch_HaveNoWarnings(string @operator, string path, object? value)
    {
        var result = Validate(Rule(Op(@operator, Value(value), Path(path))));

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Validate_OrderingAListOrObject_IsOneWarning()
    {
        var result = Validate(Rule(Op("gt", Path("Lines"), Path("Customer"))));

        Assert.Equal("'gt' can't order a list; the condition is never true.", Assert.Single(result.Warnings).Message);
    }

    [Fact]
    public void Validate_ValuesBuiltInCode_FollowTheComparisonRules()
    {
        var guid = Validate(Rule(Op("eq", Path("Number"), Value(Guid.Empty))));
        var dateOnly = Validate(Rule(Op("eq", Path("PlacedAt"), Value(new DateOnly(2026, 10, 21)))));
        var dateAsText = Validate(Rule(Op("eq", Path("Number"), Value(new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc)))));
        var list = Validate(Rule(Op("eq", Path("Total"), Value(new List<object?> { 1, 2 }))));

        Assert.Single(guid.Warnings);
        Assert.Single(dateOnly.Warnings);
        Assert.Empty(dateAsText.Warnings);
        Assert.Equal("'eq' compares a number with a list; the condition is never true.", Assert.Single(list.Warnings).Message);
    }

    [Fact]
    public void Validate_TwoPathsOfDifferentTypes_IsAWarning()
    {
        var result = Validate(Rule(Op("eq", Path("Total"), Path("Paid"))));

        Assert.Equal("'eq' compares a number with a boolean; the condition is never true.", Assert.Single(result.Warnings).Message);
    }

    [Fact]
    public void Validate_CustomOperatorNamedLikeABuiltIn_IsNotTypeChecked()
    {
        var validator = new RuleValidator(Catalog().Operators(new CustomEquals(), new AndOperator()).Build());

        var result = validator.Validate(Rule(Op("eq", Path("Total"), Value("100"))));

        Assert.Empty(result.Warnings);
    }

    private static RuleValidationResult Validate(RuleDefinition rule) => Validator.Validate(rule);

    // OrderPlaced comes from a catalog store, so its context without a provider ('unregistered') is a catalog problem.
    private static TestCatalog Catalog() => new TestCatalog()
        .Action("log", new NoOpAction())
        .Provider("customer", new FakeContextProvider((object?)null), new CustomerSchema().Build("customer"))
        .Provider("account", new FakeContextProvider((object?)null), new AccountSchema().Build("account"))
        .Provider("watchers", new FakeContextProvider((object?)null), new CustomerSchema().Build("watchers", isMany: true))
        .Provider("legacy", new FakeContextProvider((object?)null))   // registered, but without a schema
        .Stored(new CatalogDocument([new OrderPlacedSchema().Build()]));

    private static RuleDefinition Rule(ConditionNode condition, params ActionDefinition[] actions) =>
        new() { Id = "rule", Name = "Rule", EventType = "OrderPlaced", Condition = condition, Actions = [.. actions] };

    private static ActionDefinition Action(ConditionNode? condition = null, ScheduleMetadata? schedule = null) =>
        new() { Action = "log", Condition = condition, Schedule = schedule };

    public enum OrderStatus { Placed, Paid }

    public sealed class OrderDto
    {
        public decimal Total { get; set; }
        public int Quantity { get; set; }
        public bool Paid { get; set; }
        public string Number { get; set; } = "";
        public string? Note { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter<OrderStatus>))] public OrderStatus Status { get; set; }
        public DateTimeOffset PlacedAt { get; set; }
        public DateOnly DueOn { get; set; }
        public string CustomerId { get; set; } = "";
        public CustomerDto Customer { get; set; } = new();
        public List<LineDto> Lines { get; set; } = [];
        public Dictionary<string, string> Attributes { get; set; } = [];
        public object? Payload { get; set; }
        public NodeDto Node { get; set; } = new();
        public string InternalNotes { get; set; } = "";
    }

    public sealed class CustomerDto
    {
        public string Tier { get; set; } = "";
    }

    public sealed class AccountDto
    {
        public decimal Balance { get; set; }
    }

    public sealed class LineDto
    {
        public string Sku { get; set; } = "";
    }

    public sealed class NodeDto
    {
        public string Name { get; set; } = "";
        public NodeDto? Next { get; set; }
    }

    public sealed class OrderPlacedSchema : EventSchema<OrderDto>
    {
        public OrderPlacedSchema()
        {
            Event("OrderPlaced");
            Field(o => o.InternalNotes).Ignore();
            Context("customer").ReferencedBy(o => o.CustomerId);
            Context("billingAccount").LoadedBy("account");
            Context("watchers");
            Context("legacy");
            Context("unregistered");
        }
    }

    public sealed class CustomerSchema : ContextSchema<CustomerDto>;

    public sealed class AccountSchema : ContextSchema<AccountDto>;

    private sealed class CustomEquals : IConditionOperator
    {
        public string Name => "eq";
        public int MinOperands => 2;
        public int MaxOperands => 2;
        public bool Evaluate(OperatorContext context) => true;
    }

    private sealed class NoOpAction : IAction
    {
        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(ActionResult.Success());
    }
}
