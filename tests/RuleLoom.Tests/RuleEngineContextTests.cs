using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Context;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Abstractions.Scheduling;
using RuleLoom.Engine;
using static RuleLoom.Tests.Conditions;

namespace RuleLoom.Tests;

public class RuleEngineContextTests
{
    private static readonly object Order = new { CustomerId = "c-1", Total = 640.5m };

    [Fact]
    public async Task ExecuteAsync_RulesUsingAContext_LoadOnlyUsedContextsOnce()
    {
        var customer = new FakeContextProvider(new { Tier = "Gold" });
        var warehouse = new FakeContextProvider(new { City = "Paris" });
        var engine = Engine(new RecordingAction(), ("customer", customer), ("warehouse", warehouse));

        var result = await engine.ExecuteAsync("OrderPlaced", Order,
            [
                Rule("gold", Op("eq", Path("@customer.Tier"), Value("Gold"))),
                Rule("big-gold", Op("and", Op("gt", Path("Total"), Value(500)), Op("eq", Path("@Customer.Tier"), Value("Gold"))))
            ],
            TestContext.Current.CancellationToken);

        Assert.All(result.Rules, rule => Assert.Equal(RuleExecutionStatus.Matched, rule.Status));
        Assert.Equal(1, customer.Calls);
        Assert.Equal(0, warehouse.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_SeveralContexts_LoadsThemInParallel()
    {
        var gate = new TaskCompletionSource();
        var customer = new FakeContextProvider(new { Tier = "Gold" }) { Gate = gate.Task };
        var warehouse = new FakeContextProvider(new { City = "Paris" }) { Gate = gate.Task };
        var engine = Engine(new RecordingAction(), ("customer", customer), ("warehouse", warehouse));

        var execution = engine.ExecuteAsync("OrderPlaced", Order,
            [Rule("both", Op("and", Op("eq", Path("@customer.Tier"), Value("Gold")), Op("eq", Path("@warehouse.City"), Value("Paris"))))],
            TestContext.Current.CancellationToken);

        // Both loads start before either finishes.
        Assert.Equal((1, 1), (customer.Calls, warehouse.Calls));
        gate.SetResult();

        var result = await execution;
        Assert.Equal(RuleExecutionStatus.Matched, result.Rules[0].Status);
    }

    [Fact]
    public async Task ExecuteAsync_ActionConditionAndReferencePath_SeeContexts()
    {
        var action = new RecordingAction();
        var customer = new FakeContextProvider(new { Tier = "Gold", RenewsAt = "2026-11-01T00:00:00Z" });
        var engine = Engine(action, ("customer", customer));
        var rule = Rule("renewal", Op("gt", Path("Total"), Value(0)),
            new ActionDefinition { Action = "record", Condition = Op("eq", Path("@customer.Tier"), Value("Gold")) },
            new ActionDefinition
            {
                Action = "record",
                Schedule = new ScheduleMetadata { Mode = ScheduleMode.Advance, Amount = 1, Unit = ScheduleUnit.Days, ReferencePath = "@customer.RenewsAt" }
            });

        var result = await engine.ExecuteAsync("OrderPlaced", Order, [rule], TestContext.Current.CancellationToken);

        var actions = result.Rules[0].ActionResults;
        Assert.Equal(ActionExecutionStatus.ExecutedImmediately, actions[0].Status);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero), actions[1].ScheduledFor);
        Assert.Equal(1, customer.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_ProviderFails_OnlyRulesUsingTheContextFail()
    {
        var engine = Engine(new RecordingAction(), ("customer", FakeContextProvider.Failing("customers service is down")));

        var result = await engine.ExecuteAsync("OrderPlaced", Order,
            [Rule("gold", Op("eq", Path("@customer.Tier"), Value("Gold"))), Rule("big", Op("gt", Path("Total"), Value(500)))],
            TestContext.Current.CancellationToken);

        Assert.Equal(RuleExecutionStatus.Failed, result.Rules.Single(r => r.RuleId == "gold").Status);
        Assert.IsType<ContextLoadException>(result.Rules.Single(r => r.RuleId == "gold").Error);
        Assert.Equal(RuleExecutionStatus.Matched, result.Rules.Single(r => r.RuleId == "big").Status);
    }

    [Fact]
    public async Task ExecuteAsync_UndeclaredContext_FailsTheRuleWithoutLoadingIt()
    {
        var warehouse = new FakeContextProvider(new { City = "Paris" });
        var engine = new TestCatalog()
            .Action("record", new RecordingAction())
            .Provider("warehouse", warehouse)
            .Event("OrderPlaced")
            .Engine();

        var result = await engine.ExecuteAsync("OrderPlaced", Order,
            [Rule("city", Op("eq", Path("@warehouse.City"), Value("Paris")))], TestContext.Current.CancellationToken);

        Assert.Equal(RuleExecutionStatus.Failed, result.Rules[0].Status);
        Assert.Equal("'@warehouse' is not a context of OrderPlaced.", result.Rules[0].Error!.Message);
        Assert.Equal(0, warehouse.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_EventNotInTheCatalog_FailsRulesUsingContexts()
    {
        var engine = new TestCatalog()
            .Action("record", new RecordingAction())
            .Provider("customer", new FakeContextProvider(new { Tier = "Gold" }))
            .Engine();

        var result = await engine.ExecuteAsync("OrderPlaced", Order,
            [Rule("gold", Op("eq", Path("@customer.Tier"), Value("Gold"))), Rule("big", Op("gt", Path("Total"), Value(500)))],
            TestContext.Current.CancellationToken);

        Assert.Equal(RuleExecutionStatus.Failed, result.Rules[0].Status);
        Assert.Contains("isn't in the catalog", result.Rules[0].Error!.Message);
        Assert.Equal(RuleExecutionStatus.Matched, result.Rules[1].Status);
    }

    [Fact]
    public async Task ExecuteAsync_ProviderCantBeCreated_FailsRulesUsingTheContext()
    {
        var engine = new RuleEngine(
            new TestCatalog().Provider("customer", new FakeContextProvider(new { })).Event("OrderPlaced", "customer").Build());

        var result = await engine.ExecuteAsync("OrderPlaced", Order,
            [Rule("gold", Op("eq", Path("@customer.Tier"), Value("Gold")))], TestContext.Current.CancellationToken);

        Assert.Equal(RuleExecutionStatus.Failed, result.Rules[0].Status);
        Assert.Contains("parameterless constructor", Assert.IsType<ContextLoadException>(result.Rules[0].Error).InnerException!.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ActionLoadsContextOnDemand_SharesTheCache()
    {
        var watchers = new FakeContextProvider(new[] { "a@example.com", "b@example.com" });
        var customer = new FakeContextProvider(new { Tier = "Gold" });
        var action = new RecordingAction(async (context, ct) =>
        {
            var emails = (string[])(await context.GetContextAsync("watchers", ct))!;
            var tier = await context.GetContextAsync("customer", ct);
            return $"{string.Join(",", emails)} {((dynamic)tier!).Tier}";
        });
        var engine = Engine(action, ("watchers", watchers), ("customer", customer));

        var result = await engine.ExecuteAsync("OrderPlaced", Order,
            [Rule("gold", Op("eq", Path("@customer.Tier"), Value("Gold")), new ActionDefinition { Action = "record" })],
            TestContext.Current.CancellationToken);

        Assert.Equal("a@example.com,b@example.com Gold", result.Rules[0].ActionResults[0].Message);
        Assert.Equal((1, 1), (watchers.Calls, customer.Calls));
    }

    [Fact]
    public async Task ExecuteAsync_ChangedOnAContext_FailsTheRule()
    {
        var engine = Engine(new RecordingAction(), ("customer", new FakeContextProvider(new { Tier = "Gold" })));

        var result = await engine.ExecuteAsync("OrderUpdated", Order, Order,
            [Rule("tier-changed", Op("changed", Path("@customer.Tier")), eventType: "OrderUpdated")],
            TestContext.Current.CancellationToken);

        Assert.Equal(RuleExecutionStatus.Failed, result.Rules[0].Status);
        Assert.Contains("no previous state", result.Rules[0].Error!.Message);
    }

    [Fact]
    public async Task ExecuteAsync_Cancelled_PropagatesFromProviders()
    {
        var gate = new TaskCompletionSource();
        var engine = Engine(new RecordingAction(), ("customer", new FakeContextProvider(new { }) { Gate = gate.Task }));
        using var cancellation = new CancellationTokenSource();

        var execution = engine.ExecuteAsync("OrderPlaced", Order,
            [Rule("gold", Op("eq", Path("@customer.Tier"), Value("Gold")))], cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
    }

    // OrderPlaced and OrderUpdated declare every provider's context.
    private static RuleEngine Engine(RecordingAction action, params (string Name, IContextProvider Provider)[] providers)
    {
        var catalog = new TestCatalog().Action("record", action);
        foreach (var (name, provider) in providers)
            catalog.Provider(name, provider);

        var names = providers.Select(p => p.Name).ToArray();
        return catalog.Event("OrderPlaced", names).Event("OrderUpdated", names)
            .Engine(new FixedTimeProvider(new DateTimeOffset(2026, 10, 18, 9, 0, 0, TimeSpan.Zero)));
    }

    private static RuleDefinition Rule(string id, ConditionNode condition, params ActionDefinition[] actions) =>
        Rule(id, condition, "OrderPlaced", actions);

    private static RuleDefinition Rule(string id, ConditionNode condition, string eventType, params ActionDefinition[] actions) =>
        new()
        {
            Id = id,
            Name = id,
            EventType = eventType,
            Condition = condition,
            Actions = actions.Length > 0 ? [.. actions] : [new ActionDefinition { Action = "record" }]
        };

    private sealed class RecordingAction(Func<EvaluationContext, CancellationToken, Task<string?>>? run = null) : IAction
    {
        public async Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default) =>
            ActionResult.Success(run is null ? null : await run(context, cancellationToken));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
