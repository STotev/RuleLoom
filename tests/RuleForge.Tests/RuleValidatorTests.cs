using RuleForge.Abstractions;
using RuleForge.Abstractions.Action;
using RuleForge.Abstractions.Condition;
using RuleForge.Abstractions.Rule;
using RuleForge.Abstractions.Scheduling;
using RuleForge.Actions;
using RuleForge.Operators;
using RuleForge.Validation;
using static RuleForge.Tests.Conditions;

namespace RuleForge.Tests;

public class RuleValidatorTests
{
    private static readonly RuleValidator Validator = new(
        [new EqualsOperator(), new ChangedOperator(), new AndOperator(), new OrOperator(), new NotOperator()],
        new DirectActionExecutorFactory([new NoOpAction("log"), new NoOpAction("send-email")]));

    private static readonly OperatorCondition Valid = Op("eq", Path("Price"), Value(1));

    [Fact]
    public void Validate_ValidRule_IsValid()
    {
        var rule = Rule(Op("and", Valid, Op("not", Op("changed", Path("Status")))),
            Action("log", condition: Valid),
            Action("send-email", schedule: new ScheduleMetadata { Mode = ScheduleMode.Delay, Amount = 1, Unit = ScheduleUnit.Hours }));

        var result = Validator.Validate(rule);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_NamesInOtherCase_AreValid()
    {
        var result = Validator.Validate(Rule(Op("AND", Op("Eq", Path("Price"), Value(1))), Action("LOG")));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_UnknownOperator_ReportsItsLocation()
    {
        var result = Validator.Validate(Rule(Op("and", Valid, Op("gt", Path("Price"), Value(1)))));

        AssertSingleError(result, "$.condition.operands[1].operator", "Unknown operator 'gt'.");
    }

    [Fact]
    public void Validate_TooFewOperands_ReportsLimit()
    {
        var result = Validator.Validate(Rule(Op("eq", Path("Price"))));

        AssertSingleError(result, "$.condition.operands", "Operator 'eq' requires at least 2 operand(s), but has 1.");
    }

    [Fact]
    public void Validate_TooManyOperands_ReportsLimit()
    {
        var result = Validator.Validate(Rule(Op("not", Valid, Valid)));

        AssertSingleError(result, "$.condition.operands", "Operator 'not' allows at most 1 operand(s), but has 2.");
    }

    [Fact]
    public void Validate_NonOperatorRoot_IsReported()
    {
        var result = Validator.Validate(Rule(Path("IsActive")));

        AssertSingleError(result, "$.condition", "The rule's root condition must be an operator, not PathOperand.");
    }

    [Fact]
    public void Validate_UnknownActionType_ReportsItsLocation()
    {
        var result = Validator.Validate(Rule(Valid, Action("log"), Action("send-sms")));

        AssertSingleError(result, "$.actions[1].type", "No action executor is registered for action type 'send-sms'.");
    }

    [Fact]
    public void Validate_ActionCondition_IsValidatedLikeRuleCondition()
    {
        var result = Validator.Validate(Rule(Valid, Action("log", condition: Op("or", Op("missing")))));

        AssertSingleError(result, "$.actions[0].condition.operands[0].operator", "Unknown operator 'missing'.");
    }

    [Fact]
    public void Validate_IncompleteSchedule_ReportsMissingAmountAndUnit()
    {
        var result = Validator.Validate(Rule(Valid, Action("log", schedule: new ScheduleMetadata { Mode = ScheduleMode.Advance })));

        Assert.Equal(
            [
                new RuleValidationError("$.actions[0].schedule.amount", "'amount' is required for 'Advance' schedules."),
                new RuleValidationError("$.actions[0].schedule.unit", "'unit' is required for 'Advance' schedules.")
            ],
            result.Errors);
    }

    [Fact]
    public void Validate_EmptyNameAndEventType_AreReported()
    {
        var rule = new RuleDefinition { Name = " ", EventType = "", Condition = Valid };

        Assert.Equal(["$.name", "$.eventType"], Validator.Validate(rule).Errors.Select(e => e.Path));
    }

    [Fact]
    public void Validate_SeveralProblems_ReportsAllInDocumentOrder()
    {
        var rule = Rule(
            Op("and", Op("unknown", Op("eq", Path("A"))), Op("eq", Path("B"), Value(1))),
            Action("send-sms"),
            Action("log", condition: Op("nope")));

        var result = Validator.Validate(rule);

        Assert.False(result.IsValid);
        Assert.Equal(
            [
                "$.condition.operands[0].operator",
                "$.condition.operands[0].operands[0].operands",
                "$.actions[0].type",
                "$.actions[1].condition.operator"
            ],
            result.Errors.Select(e => e.Path));
    }

    [Fact]
    public void Constructor_DuplicateOperatorNames_Throws()
    {
        Assert.Throws<ArgumentException>(() => new RuleValidator(
            [new EqualsOperator(), new EqualsOperator()], new DirectActionExecutorFactory([])));
    }

    [Fact]
    public void DirectActionExecutorFactory_ActionTypes_ListsRegisteredTypes()
    {
        var factory = new DirectActionExecutorFactory([new NoOpAction("log"), new NoOpAction("send-email")]);

        Assert.Equal(["log", "send-email"], factory.ActionTypes.Order());
    }

    private static void AssertSingleError(RuleValidationResult result, string path, string message)
    {
        Assert.False(result.IsValid);
        Assert.Equal(new RuleValidationError(path, message), Assert.Single(result.Errors));
    }

    private static RuleDefinition Rule(ConditionNode condition, params ActionDefinition[] actions) =>
        new() { Name = "Rule", EventType = "Event", Condition = condition, Actions = [.. actions] };

    private static ActionDefinition Action(string type, ConditionNode? condition = null, ScheduleMetadata? schedule = null) =>
        new() { Type = type, Condition = condition, Schedule = schedule };

    private sealed class NoOpAction(string type) : IActionExecutor
    {
        public string Type => type;

        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(ActionResult.Success());
    }
}
