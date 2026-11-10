using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Abstractions.Scheduling;
using RuleLoom.Engine;
using RuleLoom.Schemas;
using static RuleLoom.Tests.Conditions;

namespace RuleLoom.Tests;

// A payment reminder scheduled when an order was placed, run when it's due against the order loaded then.
public class ExecuteScheduledTests
{
    private static readonly DateTimeOffset Due = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly ScheduledAction Reminder = new("reminder", 0, "notify", Due, "reminder:0:SO-1");

    private static readonly RuleEvent Placed = new("OrderPlaced",
        new Order("SO-1", "AwaitingPayment", "ada@example.com"), new Order("SO-1", "New", "ada@example.com"));

    private readonly NotifyAction _notify = new();

    [Fact]
    public async Task ExecuteScheduledAsync_StillTrueOnCurrentData_RunsTheAction_AgainstThatData()
    {
        var orders = new FakeContextProvider(new Order("SO-1", "AwaitingPayment", "ada@new.example.com"));

        var result = await Engine(orders).ExecuteScheduledAsync(Reminder, Placed, [Rule()], TestContext.Current.CancellationToken);

        Assert.Equal((ActionExecutionStatus.Executed, "notify", 0), (result.Status, result.ActionName, result.ActionIndex));
        Assert.Equal("ada@new.example.com", _notify.To);
        var request = Assert.Single(orders.Requests);
        Assert.Equal(("order", "SO-1"), (request.Name, request.Reference));
        Assert.Same(Placed, request.Event);
    }

    [Fact]
    public async Task ExecuteScheduledAsync_RuleNoLongerMatchesCurrentData_IsSkipped()
    {
        var orders = new FakeContextProvider(new Order("SO-1", "Paid", "ada@example.com"));

        var result = await Engine(orders).ExecuteScheduledAsync(Reminder, Placed, [Rule()], TestContext.Current.CancellationToken);

        Assert.Equal((ActionExecutionStatus.Skipped, "Action 'notify' skipped: rule 'reminder' no longer matches."), (result.Status, result.Message));
        Assert.Null(_notify.To);
    }

    [Theory]
    [InlineData("Paid", ActionExecutionStatus.Executed)]
    [InlineData("New", ActionExecutionStatus.Skipped)]   // the change was undone
    public async Task ExecuteScheduledAsync_Changed_ComparesCurrentDataWithBeforeTheEvent(string status, ActionExecutionStatus expected)
    {
        var orders = new FakeContextProvider(new Order("SO-1", status, "ada@example.com"));
        var rule = Rule(Op("changed", Path("Status")));

        var result = await Engine(orders).ExecuteScheduledAsync(Reminder, Placed, [rule], TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task ExecuteScheduledAsync_ActionConditionNoLongerHolds_IsSkipped()
    {
        var orders = new FakeContextProvider(new Order("SO-1", "AwaitingPayment", "unknown"));
        var rule = Rule(actionCondition: Op("eq", Path("Email"), Value("ada@example.com")));

        var result = await Engine(orders).ExecuteScheduledAsync(Reminder, Placed, [rule], TestContext.Current.CancellationToken);

        Assert.Equal(ActionExecutionStatus.Skipped, result.Status);
    }

    [Fact]
    public async Task ExecuteScheduledAsync_SubjectNoLongerExists_IsSkipped()
    {
        var result = await Engine(new FakeContextProvider((object?)null))
            .ExecuteScheduledAsync(Reminder, Placed, [Rule()], TestContext.Current.CancellationToken);

        Assert.Equal((ActionExecutionStatus.Skipped, "Action 'notify' skipped: its subject no longer exists (its loader found none)."),
            (result.Status, result.Message));
    }

    [Fact]
    public async Task ExecuteScheduledAsync_LoaderFails_FailsTheAction()
    {
        var orders = new FakeContextProvider(_ => throw new TimeoutException("database down"));

        var result = await Engine(orders).ExecuteScheduledAsync(Reminder, Placed, [Rule()], TestContext.Current.CancellationToken);

        Assert.Equal(ActionExecutionStatus.Failed, result.Status);
        Assert.IsType<TimeoutException>(result.Error);
    }

    [Fact]
    public async Task ExecuteScheduledAsync_RuleGone_IsSkipped()
    {
        var other = Rule(id: "other");

        var result = await Engine(new FakeContextProvider((object?)null))
            .ExecuteScheduledAsync(Reminder, Placed, [other], TestContext.Current.CancellationToken);

        Assert.Equal((ActionExecutionStatus.Skipped, "Rule 'reminder' is no longer active."), (result.Status, result.Message));
    }

    [Theory]
    [InlineData("log", ScheduleMode.Delay)]          // another action at that index
    [InlineData("notify", ScheduleMode.Immediate)]   // no longer scheduled
    public async Task ExecuteScheduledAsync_RuleEditedSoTheActionIsGone_IsSkipped(string action, ScheduleMode mode)
    {
        var edited = Rule(actions: [new ActionDefinition { Action = action, Schedule = new ScheduleMetadata { Mode = mode, Amount = 1, Unit = ScheduleUnit.Hours } }]);

        var result = await Engine(new FakeContextProvider((object?)null))
            .ExecuteScheduledAsync(Reminder, Placed, [edited], TestContext.Current.CancellationToken);

        Assert.Equal((ActionExecutionStatus.Skipped, "Rule 'reminder' no longer schedules action 0 ('notify'): its actions changed."),
            (result.Status, result.Message));
    }

    [Fact]
    public async Task ExecuteScheduledAsync_WithoutALoader_ChecksTheStoredEvent()
    {
        var engine = new TestCatalog().Action("notify", _notify).Engine();

        var result = await engine.ExecuteScheduledAsync(Reminder, Placed, [Rule()], TestContext.Current.CancellationToken);

        Assert.Equal(ActionExecutionStatus.Executed, result.Status);
        Assert.Equal("ada@example.com", _notify.To);
    }

    [Fact]
    public async Task ExecuteScheduledAsync_EditedParameters_TheCurrentVersionRuns()
    {
        var orders = new FakeContextProvider(new Order("SO-1", "AwaitingPayment", "ada@example.com"));
        var edited = Rule(actions: [Notify(RulePath.Parse("Email"), "Corrected")]);

        await Engine(orders).ExecuteScheduledAsync(Reminder, Placed, [edited], TestContext.Current.CancellationToken);

        Assert.Equal("Corrected", _notify.Subject);
    }

    [Fact]
    public async Task ExecuteScheduledAsync_LoaderNotRegistered_FailsTheAction()
    {
        // Possible only for an event schema from a catalog store: in code, the catalog's build fails.
        var engine = new TestCatalog().Action("notify", _notify).Stored(new CatalogDocument([OrderPlaced])).Engine();

        var result = await engine.ExecuteScheduledAsync(Reminder, Placed, [Rule()], TestContext.Current.CancellationToken);

        Assert.Equal(ActionExecutionStatus.Failed, result.Status);
        Assert.Equal("No context provider is registered under 'order' (the subject's loader).", result.Error!.Message);
    }

    private static readonly EventSchemaDefinition OrderPlaced = EventSchemaDefinition.Parse("""
        { "x-ruleloom-event": "OrderPlaced", "type": "object",
          "properties": { "number": { "type": "string" }, "status": { "type": "string" }, "email": { "type": "string" } },
          "x-ruleloom-subject": { "x-ruleloom-identity": "number", "x-ruleloom-provider": "order" } }
        """);

    private RuleEngine Engine(FakeContextProvider orders) =>
        new TestCatalog().Action("notify", _notify).Provider("order", orders).Event(OrderPlaced).Engine();

    private static RuleDefinition Rule(ConditionNode? condition = null, ConditionNode? actionCondition = null, string id = "reminder",
        List<ActionDefinition>? actions = null) => new()
    {
        Id = id, Name = "Reminder", EventType = "OrderPlaced",
        Condition = condition ?? Op("eq", Path("Status"), Value("AwaitingPayment")),
        Actions = actions ?? [Notify(RulePath.Parse("Email"), "Your payment is due", actionCondition)]
    };

    private static ActionDefinition Notify(object to, string subject, ConditionNode? condition = null) => new()
    {
        Action = "notify",
        Condition = condition,
        Parameters = { ["to"] = to, ["subject"] = subject },
        Schedule = new ScheduleMetadata { Mode = ScheduleMode.Delay, Amount = 1, Unit = ScheduleUnit.Hours }
    };

    public sealed record Order(string Number, string Status, string Email);

    private sealed class NotifyAction : IAction
    {
        public string? To { get; private set; }
        public string? Subject { get; private set; }

        public async Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default)
        {
            To = await action.GetParameterAsync<string>("to", context, cancellationToken);
            Subject = action.GetParameter<string>("subject");
            return ActionResult.Success();
        }
    }
}
