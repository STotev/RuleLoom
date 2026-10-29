using System.Text.Json;
using System.Text.Json.Nodes;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Abstractions.Scheduling;
using RuleLoom.Validation;
using static RuleLoom.Tests.Conditions;

namespace RuleLoom.Tests;

public class RuleEventTests
{
    private static readonly DateTimeOffset NineInSofia = new(2026, 7, 1, 9, 0, 0, TimeSpan.FromHours(3));

    private static readonly RuleEvent Placed = new("OrderPlaced", new Order("SO-1001", 640.5m))
    {
        EventId = "msg-1",
        Subject = "order-1001",
        Source = "shop",
        OccurredAt = NineInSofia,
        Metadata = new Dictionary<string, object?> { ["tenantId"] = "acme-shop", ["region"] = "EU" }
    };

    [Fact]
    public void ToJson_Parse_RoundTrips_StateAsJson()
    {
        var parsed = RuleEvent.Parse(Placed.ToJson());

        Assert.Equal(("OrderPlaced", "msg-1", "order-1001", "shop", NineInSofia), (parsed.EventType, parsed.EventId, parsed.Subject, parsed.Source, parsed.OccurredAt));
        Assert.Equal(["acme-shop", "EU"], parsed.Metadata.Values);
        Assert.Equal(640.5m, new EvaluationContext(parsed.Current).Resolve("Total"));
        Assert.Null(parsed.Previous);
    }

    [Fact]
    public void ToJson_WritesOnlyWhatsSet()
    {
        var json = JsonNode.Parse(new RuleEvent("OrderPlaced", new Order("SO-1", 1m)).ToJson())!.AsObject();

        Assert.Equal(["eventType", "current"], json.Select(p => p.Key));
    }

    [Fact]
    public void ToJson_PreviousFromChanges_IsWrittenAsChanges_AndReadBack()
    {
        var current = JsonDocument.Parse("""{ "Status": "Paid", "Total": 10 }""").RootElement;
        var changes = JsonDocument.Parse("""{ "Status": "AwaitingPayment" }""").RootElement;
        var updated = new RuleEvent("OrderUpdated", current, PreviousState.FromChanges(current, changes));

        var json = JsonNode.Parse(updated.ToJson())!;
        var parsed = RuleEvent.Parse(updated.ToJson());
        var context = new EvaluationContext(parsed.Current, parsed.Previous);

        Assert.Null(json["previous"]);
        Assert.Equal("AwaitingPayment", (string?)json["changes"]!["Status"]);
        Assert.IsType<PreviousState>(parsed.Previous);
        Assert.Equal(("AwaitingPayment", 10L), (context.Resolve("$previous.Status"), context.Resolve("$previous.Total")));
    }

    [Fact]
    public void Parse_FullPrevious_NamesIgnoringCase()
    {
        var parsed = RuleEvent.Parse("""{ "EVENTTYPE": "OrderUpdated", "Current": { "Status": "Paid" }, "previous": { "Status": "New" } }""");

        Assert.Equal("New", new EvaluationContext(parsed.Current, parsed.Previous).Resolve("$previous.Status"));
    }

    [Theory]
    [InlineData("[]", "An event must be a JSON object.")]
    [InlineData("""{ "current": {} }""", "An event needs a non-empty 'eventType'.")]
    [InlineData("""{ "eventType": "E" }""", "An event needs its 'current' state.")]
    [InlineData("""{ "eventType": "E", "current": {}, "previous": {}, "changes": {} }""", "An event has either 'previous' or 'changes', not both.")]
    [InlineData("""{ "eventType": "E", "current": {}, "changes": 1 }""", "'changes' must be a JSON object.")]
    [InlineData("""{ "eventType": "E", "current": {}, "metadata": [] }""", "'metadata' must be a JSON object.")]
    [InlineData("""{ "eventType": "E", "current": {}, "subject": 5 }""", "'subject' must be a string.")]
    [InlineData("""{ "eventType": "E", "current": {}, "occurredAt": "2026-07-01T09:00:00" }""", "'occurredAt' must be an ISO 8601 date-time with an offset, e.g. 2026-05-01T09:00:00Z: '2026-07-01T09:00:00'.")]
    public void Parse_NotAnEvent_Throws(string json, string message)
    {
        var ex = Assert.Throws<JsonException>(() => RuleEvent.Parse(json));

        Assert.Equal(message, ex.Message);
    }

    [Theory]
    [InlineData("$event.Subject", "order-1001")]
    [InlineData("$EVENT.subject", "order-1001")]
    [InlineData("$event.EventType", "OrderPlaced")]
    [InlineData("$event.Metadata.region", "EU")]
    [InlineData("$event.Metadata.TENANTID", "acme-shop")]
    [InlineData("$event.Metadata.missing", null)]
    [InlineData("$event.Unknown", null)]
    public void Resolve_EventPaths_ReadTheEnvelope(string path, object? expected)
    {
        var context = new EvaluationContext(Placed, new TestCatalog().Build(), new TestCatalog());

        Assert.Equal(expected, context.Resolve(path));
        Assert.Same(Placed, context.Event);
    }

    [Fact]
    public void Resolve_EventPath_WithoutAnEvent_IsNull()
    {
        Assert.Null(new EvaluationContext(new { }).Resolve("$event.Subject"));
    }

    [Fact]
    public void RulePath_Event_ParsesAndPrints_AndHasNoState()
    {
        var path = RulePath.Parse("$Event.Metadata.region");

        Assert.Equal((PathRoot.Event, "Metadata.region"), (path.Root, path.Property));
        Assert.Equal("$event.Metadata.region", path.ToString());
        Assert.Throws<InvalidOperationException>(() => path.InState(PathRoot.Previous));
    }

    [Theory]
    [InlineData("$previous.$event.Subject", "The event's envelope has no current or previous state; use '$event.Subject' instead of '$previous.$event.Subject'.")]
    [InlineData("$current.$event", "The event's envelope has no current or previous state; use '$event' instead of '$current.$event'.")]
    public void RulePath_EventAfterAState_IsInvalid(string text, string message)
    {
        Assert.False(RulePath.TryParse(text, out _, out var error));
        Assert.Equal(message, error);
    }

    [Fact]
    public async Task ExecuteAsync_RulesReadTheEnvelope_ActionsSeeTheEvent()
    {
        var action = new RecordingAction();
        var engine = new TestCatalog().Action("record", action).Engine();
        var rule = new RuleDefinition
        {
            Id = "eu-orders", Name = "EU orders", EventType = "OrderPlaced",
            Condition = Op("and", Op("eq", Path("$event.Metadata.region"), Value("EU")), Op("eq", Path("$event.Subject"), Value("order-1001"))),
            Actions = [new ActionDefinition { Action = "record" }]
        };

        var result = await engine.ExecuteAsync(Placed, [rule], TestContext.Current.CancellationToken);

        Assert.Equal(RuleExecutionStatus.Matched, Assert.Single(result.Rules).Status);
        Assert.Same(Placed, action.Event);
    }

    [Fact]
    public async Task ExecuteAsync_StateOverload_IsAnEventWithoutEnvelopeData()
    {
        var action = new RecordingAction();
        var engine = new TestCatalog().Action("record", action).Engine();
        var rule = new RuleDefinition
        {
            Id = "r", Name = "R", EventType = "OrderPlaced",
            Condition = Op("eq", Path("$event.EventType"), Value("OrderPlaced")),
            Actions = [new ActionDefinition { Action = "record" }]
        };

        await engine.ExecuteAsync("OrderPlaced", new Order("SO-1", 1m), [rule], TestContext.Current.CancellationToken);

        Assert.Equal(("OrderPlaced", null), (action.Event!.EventType, action.Event.Subject));
    }

    [Fact]
    public async Task ExecuteAsync_ScheduleFromTheEventsTime()
    {
        var engine = new TestCatalog().Action("record", new RecordingAction()).Engine();
        var rule = new RuleDefinition
        {
            Id = "r", Name = "R", EventType = "OrderPlaced", Condition = Op("eq", Value(1), Value(1)),
            Actions = [new ActionDefinition
            {
                Action = "record",
                Schedule = new ScheduleMetadata { Mode = ScheduleMode.Delay, Amount = 2, Unit = ScheduleUnit.Hours, ReferencePath = "$event.OccurredAt" }
            }]
        };

        var result = await engine.ExecuteAsync(Placed, [rule], TestContext.Current.CancellationToken);

        Assert.Equal(NineInSofia.AddHours(2), Assert.Single(result.ScheduledActions).Result.ScheduledFor);
    }

    [Fact]
    public async Task ContextProviders_GetTheEvent()
    {
        var provider = new FakeContextProvider(new { Tier = "gold" });
        var catalog = new TestCatalog().Provider("customer", provider).Event("OrderPlaced", "customer");
        var context = new EvaluationContext(Placed, catalog.Build(), catalog);

        await context.GetContextAsync("customer", TestContext.Current.CancellationToken);

        Assert.Equal("acme-shop", Assert.Single(provider.Requests).Event!.Metadata["tenantId"]);
    }

    [Theory]
    [InlineData("$event.Subject", null)]
    [InlineData("$event.Metadata.anything.below", null)]
    [InlineData("$event", null)]
    [InlineData("$event.Subjct", "'Subjct' is not a field of the event's envelope ($event).")]
    [InlineData("$event.Subject.Length", "'Subject' is text and has no fields ('$event.Subject.Length').")]
    public void Validate_EventPaths_AreCheckedAgainstTheEnvelope(string path, string? error)
    {
        var validator = new RuleValidator(new TestCatalog().Event("OrderPlaced").Build());
        var rule = new RuleDefinition { Id = "r", Name = "R", EventType = "OrderPlaced", Condition = Op("eq", Path(path), Value(null)) };

        var result = validator.Validate(rule);

        Assert.Equal(error, result.Errors.SingleOrDefault()?.Message);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Validate_EventsTime_IsADateTime()
    {
        var validator = new RuleValidator(new TestCatalog().Event("OrderPlaced").Build());
        var rule = new RuleDefinition { Id = "r", Name = "R", EventType = "OrderPlaced", Condition = Op("gt", Path("$event.OccurredAt"), Value("yesterday")) };

        var warning = Assert.Single(validator.Validate(rule).Warnings);

        Assert.Equal("'gt' compares a date and time with the text 'yesterday'; the condition is never true.", warning.Message);
    }

    [Fact]
    public void Validate_ChangedOnTheEnvelope_IsAnError()
    {
        var validator = new RuleValidator(new TestCatalog().Event("OrderPlaced").Build());
        var rule = new RuleDefinition { Id = "r", Name = "R", EventType = "OrderPlaced", Condition = Op("changed", Path("$event.Subject")) };

        Assert.Equal("'changed' can't be used on the event's envelope ('$event.Subject'): it has no previous state.",
            Assert.Single(validator.Validate(rule).Errors).Message);
    }

    public sealed record Order(string Number, decimal Total);

    private sealed class RecordingAction : IAction
    {
        public RuleEvent? Event { get; private set; }

        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default)
        {
            Event = context.Event;
            return Task.FromResult(ActionResult.Success());
        }
    }
}
