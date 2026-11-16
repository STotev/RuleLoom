using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Abstractions.Scheduling;
using RuleLoom.Engine;
using static RuleLoom.Tests.Conditions;

namespace RuleLoom.Tests;

// Each test uses its own event type: listeners are process-wide and tests run in parallel.
public sealed class ObservabilityTests : IDisposable
{
    private const string Secret = "secret@example.com";

    private readonly string _eventType = $"Obs{Guid.NewGuid():N}";
    private readonly ConcurrentQueue<Activity> _activities = new();
    private readonly ConcurrentQueue<(string Instrument, long Value, Dictionary<string, object?> Tags)> _measurements = new();
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener = new();
    private readonly TestLoggerFactory _logs = new();

    public ObservabilityTests()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == RuleLoomDiagnostics.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _activities.Enqueue(activity)
        };
        ActivitySource.AddActivityListener(_activityListener);

        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == RuleLoomDiagnostics.Name)
                listener.EnableMeasurementEvents(instrument);
        };
        _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            _measurements.Enqueue((instrument.Name, value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
        _meterListener.Start();
    }

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_TracesTheEventItsActionsAndContexts()
    {
        var engine = Catalog().Engine(loggerFactory: _logs);
        var placed = new RuleEvent(_eventType, new { Total = 10, Email = Secret }) { Subject = "order-1", EventId = "msg-1" };

        await engine.ExecuteAsync(placed, [Rule("big", Op("eq", Path("@customer.Tier"), Value("gold"))), Rule("broken", Op("gt", Path("Total")))],
            TestContext.Current.CancellationToken);

        var execute = Mine("ruleloom.execute").Single();
        Assert.Equal(("order-1", "msg-1"), (execute.GetTagItem("ruleloom.subject"), execute.GetTagItem("ruleloom.event_id")));
        Assert.Equal((1, 1, 1), (execute.GetTagItem("ruleloom.rules.matched"), execute.GetTagItem("ruleloom.rules.failed"), execute.GetTagItem("ruleloom.actions.executed")));
        Assert.Equal(ActivityStatusCode.Error, execute.Status);
        Assert.Equal("broken", Assert.Single(execute.Events, e => e.Name == "ruleloom.rule_failed").Tags.Single(t => t.Key == "ruleloom.rule_id").Value);

        var action = Assert.Single(_activities, a => a.OperationName == "ruleloom.action" && a.ParentSpanId == execute.SpanId);
        Assert.Equal(("notify", "big", "Executed"), (action.GetTagItem("ruleloom.action"), action.GetTagItem("ruleloom.rule_id"), action.GetTagItem("ruleloom.status")));
        Assert.Contains(_activities, a => a.OperationName == "ruleloom.context" && a.ParentSpanId == execute.SpanId && (string?)a.GetTagItem("ruleloom.context") == "customer");
    }

    [Fact]
    public async Task ExecuteAsync_CountsRulesAndActions()
    {
        var engine = Catalog().Engine();

        await engine.ExecuteAsync(new RuleEvent(_eventType, new { Total = 10 }),
            [Rule("big", Op("eq", Value(1), Value(1))), Rule("small", Op("eq", Value(1), Value(2)))], TestContext.Current.CancellationToken);

        var mine = _measurements.Where(m => Equals(m.Tags.GetValueOrDefault("ruleloom.event_type"), _eventType)).ToList();
        Assert.Equal(["matched", "not_matched"], mine.Where(m => m.Instrument == "ruleloom.rules").Select(m => m.Tags["ruleloom.outcome"]).Order());
        var action = Assert.Single(mine, m => m.Instrument == "ruleloom.actions");
        Assert.Equal(("notify", "Executed"), (action.Tags["ruleloom.action"], action.Tags["ruleloom.status"]));
    }

    [Fact]
    public async Task ExecuteScheduledAsync_TracesTheRunWithItsStatusAndReason()
    {
        var engine = Catalog().Engine(loggerFactory: _logs);
        var scheduled = new ScheduledAction("gone", 0, "notify", DateTimeOffset.UnixEpoch, null);

        await engine.ExecuteScheduledAsync(scheduled, new RuleEvent(_eventType, new { }), [], TestContext.Current.CancellationToken);

        var run = Mine("ruleloom.execute_scheduled").Single();
        Assert.Equal(("gone", "notify", "Skipped", "Rule 'gone' is no longer active."),
            (run.GetTagItem("ruleloom.rule_id"), run.GetTagItem("ruleloom.action"), run.GetTagItem("ruleloom.status"), run.GetTagItem("ruleloom.reason")));
    }

    [Fact]
    public async Task Logs_FailuresAsWarnings_OutcomesAtDebug_NeverStateValues()
    {
        var engine = Catalog().Engine(loggerFactory: _logs);
        var placed = new RuleEvent(_eventType, new { Total = 10, Email = Secret }) { Subject = "order-1" };

        await engine.ExecuteAsync(placed, [Rule("big", Op("eq", Path("Email"), Path("Email"))), Rule("broken", Op("gt", Path("Total")))],
            TestContext.Current.CancellationToken);

        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Warning && e.Message.StartsWith($"Rule 'broken' failed for {_eventType}"));
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Debug && e.Message.StartsWith($"Rule 'big' for {_eventType}: Matched (subject order-1, event "));
        Assert.DoesNotContain(_logs.Entries, e => e.Message.Contains(Secret));
        Assert.DoesNotContain(Mine("ruleloom.execute").SelectMany(a => a.TagObjects.Concat(a.Events.SelectMany(e => e.Tags))),
            tag => tag.Value?.ToString()?.Contains(Secret) == true);
    }

    [Fact]
    public async Task Logs_SubjectOnlyAtDebug()
    {
        _logs.MinimumLevel = LogLevel.Information;
        var engine = Catalog().Engine(loggerFactory: _logs);

        await engine.ExecuteAsync(new RuleEvent(_eventType, new { Total = 10 }) { Subject = "order-1" }, [Rule("broken", Op("gt", Path("Total")))],
            TestContext.Current.CancellationToken);

        Assert.NotEmpty(_logs.Entries);
        Assert.DoesNotContain(_logs.Entries, e => e.Message.Contains("order-1"));
    }

    private List<Activity> Mine(string name) =>
        _activities.Where(a => a.OperationName == name && (string?)a.GetTagItem("ruleloom.event_type") == _eventType).ToList();

    private TestCatalog Catalog() => new TestCatalog()
        .Action("notify", new NotifyAction())
        .Provider("customer", new FakeContextProvider(new { Tier = "gold" }))
        .Event(_eventType, "customer");

    private RuleDefinition Rule(string id, Abstractions.Condition.ConditionNode condition) => new()
    {
        Id = id, Name = id, EventType = _eventType, Condition = condition,
        Actions = [new ActionDefinition { Action = "notify", Parameters = { ["to"] = RulePath.Parse("Email") } }]
    };

    private sealed class NotifyAction : IAction
    {
        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActionResult.Success());
    }

    private sealed class TestLoggerFactory : ILoggerFactory, ILogger
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();
        public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= MinimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                Entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
