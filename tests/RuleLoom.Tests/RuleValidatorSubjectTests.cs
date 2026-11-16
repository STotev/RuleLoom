using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Abstractions.Scheduling;
using RuleLoom.Schemas;
using RuleLoom.Validation;
using static RuleLoom.Tests.Conditions;

namespace RuleLoom.Tests;

// The event carries ChangedBy and PlacedAt; the order its loader returns doesn't.
public class RuleValidatorSubjectTests
{
    private const string Missing = "'ChangedBy' is not a field of the subject loaded by 'order' when the action runs.";

    private static readonly RuleValidator Validator = new(new TestCatalog()
        .Action("notify", new NoOpAction())
        .Provider("order", new FakeContextProvider((object?)null), ContextSchemaDefinition.Parse("""
            { "x-ruleloom-context": "order", "type": "object",
              "properties": { "number": { "type": "string" }, "status": { "type": "string" } } }
            """))
        .Event(EventSchemaDefinition.Parse("""
            { "x-ruleloom-event": "OrderUpdated", "type": "object",
              "properties": { "number": { "type": "string" }, "status": { "type": "string" }, "changedBy": { "type": "string" },
                "placedAt": { "type": "string", "format": "date-time" } },
              "x-ruleloom-subject": { "x-ruleloom-identity": "number", "x-ruleloom-provider": "order" } }
            """))
        .Build());

    [Fact]
    public void Validate_RuleThatSchedules_ItsConditionIsCheckedAgainstTheSubject()
    {
        var result = Validator.Validate(Rule(Op("eq", Path("ChangedBy"), Value("ada")), Scheduled()));

        Assert.Equal(new RuleValidationError("$.condition.operands[0].path", Missing), Assert.Single(result.Errors));
    }

    [Fact]
    public void Validate_RuleThatDoesntSchedule_IsCheckedAgainstTheEventOnly()
    {
        var result = Validator.Validate(Rule(Op("eq", Path("ChangedBy"), Value("ada")), Immediate(RulePath.Parse("ChangedBy"))));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ScheduledActionsConditionAndParameters_AreChecked_OtherActionsArent()
    {
        var scheduled = Scheduled(RulePath.Parse("ChangedBy"), condition: Op("eq", Path("$current.ChangedBy"), Value("ada")));

        var result = Validator.Validate(Rule(Op("eq", Path("Status"), Value("Paid")), Immediate(RulePath.Parse("ChangedBy")), scheduled));

        Assert.Equal(["$.actions[1].condition.operands[0].path", "$.actions[1].parameters.to.$path"], result.Errors.Select(e => e.Path));
        Assert.All(result.Errors, e => Assert.Equal(Missing, e.Message));
    }

    [Fact]
    public void Validate_PathsReadWhenScheduled_PreviousState_AndOtherRoots_AreNotChecked()
    {
        var scheduled = Scheduled(RulePath.Parse("$previous.ChangedBy"), referencePath: "PlacedAt");

        var result = Validator.Validate(Rule(Op("eq", Path("$event.Subject"), Value("SO-1")), scheduled));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_PathMissingFromTheEventToo_IsReportedOnce()
    {
        var result = Validator.Validate(Rule(Op("eq", Path("Statuss"), Value("Paid")), Scheduled()));

        Assert.Equal("'Statuss' is not a field of OrderUpdated.", Assert.Single(result.Errors).Message);
    }

    private static RuleDefinition Rule(ConditionNode condition, params ActionDefinition[] actions) =>
        new() { Id = "r", Name = "R", EventType = "OrderUpdated", Condition = condition, Actions = [.. actions] };

    private static ActionDefinition Immediate(object? to = null) =>
        new() { Action = "notify", Parameters = { ["to"] = to ?? "ops@example.com" } };

    private static ActionDefinition Scheduled(object? to = null, ConditionNode? condition = null, string? referencePath = null) => new()
    {
        Action = "notify",
        Condition = condition,
        Parameters = { ["to"] = to ?? "ops@example.com" },
        Schedule = new ScheduleMetadata { Mode = ScheduleMode.Delay, Amount = 1, Unit = ScheduleUnit.Hours, ReferencePath = referencePath }
    };

    private sealed class NoOpAction : IAction
    {
        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActionResult.Success());
    }
}
