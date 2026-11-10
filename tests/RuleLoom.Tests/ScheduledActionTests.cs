using System.Text.Json;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Abstractions.Scheduling;
using RuleLoom.Schemas;
using static RuleLoom.Tests.Conditions;

namespace RuleLoom.Tests;

public class ScheduledActionTests
{
    private static readonly DateTimeOffset Due = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("payment-reminder", "order-1001", "payment-reminder:0:order-1001")]
    [InlineData("a:b", "x;y=z\\", @"a\:b:0:x\;y\=z\\")]
    public void CreateKey_IsRuleActionAndSubject_Escaped(string ruleId, string subject, string expected)
    {
        Assert.Equal(expected, ScheduledAction.CreateKey(ruleId, 0, SubjectIdentity.From(subject)));
    }

    [Fact]
    public void CreateKey_WithoutASubject_IsNull()
    {
        Assert.Null(ScheduledAction.CreateKey("rule", 0, null));
        Assert.Null(SubjectIdentity.From((object?)null));
    }

    [Fact]
    public void SubjectIdentity_Composite_PartsInOrder_InvariantValues_NullWhenAPartIsMissing()
    {
        var parts = new Dictionary<string, object?> { ["country"] = "BG", ["postcode"] = 1000, ["at"] = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc) };

        Assert.Equal(@"country=BG;postcode=1000;at=2026-07-01T09\:00\:00.0000000Z", SubjectIdentity.From(parts)!.Text);
        Assert.Null(SubjectIdentity.From(new Dictionary<string, object?> { ["country"] = "BG", ["postcode"] = null }));
        Assert.Equal("1.5", SubjectIdentity.From(1.5m)!.Text);
    }

    [Fact]
    public void ToJson_Parse_RoundTrips()
    {
        var scheduled = new ScheduledAction("payment-reminder", 1, "send-email", Due, "payment-reminder:1:order-1001");

        Assert.Equal(scheduled, ScheduledAction.Parse(scheduled.ToJson()));
        Assert.Equal(new ScheduledAction("r", 0, "log", Due, null), ScheduledAction.Parse(new ScheduledAction("r", 0, "log", Due, null).ToJson()));
    }

    [Theory]
    [InlineData("[]", "A scheduled action must be a JSON object.")]
    [InlineData("""{ "actionIndex": 0, "actionName": "log", "scheduledFor": "2026-07-01T09:00:00.0000000+00:00" }""", "A scheduled action needs a non-empty 'ruleId'.")]
    [InlineData("""{ "ruleId": "r", "actionIndex": -1, "actionName": "log", "scheduledFor": "2026-07-01T09:00:00.0000000+00:00" }""", "'actionIndex' must be a non-negative whole number.")]
    [InlineData("""{ "ruleId": "r", "actionIndex": 0, "actionName": "log", "scheduledFor": "tomorrow" }""", "'scheduledFor' must be a round-trip ISO 8601 date-time (as ToJson writes it): 'tomorrow'.")]
    public void Parse_NotAScheduledAction_Throws(string json, string message)
    {
        Assert.Equal(message, Assert.Throws<JsonException>(() => ScheduledAction.Parse(json)).Message);
    }

    [Fact]
    public async Task ExecuteAsync_SubjectDeclaredByTheSchema_KeysScheduledActions_OverTheEnvelopesSubject()
    {
        var catalog = new TestCatalog().Action("log", new NoOpAction()).Event(Schema("""{ "x-ruleloom-identity": "number" }"""));
        var @event = new RuleEvent("OrderPlaced", new { Number = "SO-1001" }) { Subject = "ignored" };

        var scheduled = await ScheduleAsync(catalog, @event);

        Assert.Equal(new ScheduledAction("reminder", 1, "log", Due, "reminder:1:SO-1001"), scheduled);
    }

    [Fact]
    public async Task ExecuteAsync_CompositeSubject_KeysWithItsParts()
    {
        var catalog = new TestCatalog().Action("log", new NoOpAction())
            .Event(Schema("""{ "x-ruleloom-identity": { "country": "address.country", "postcode": "address.postcode" } }"""));
        var @event = new RuleEvent("OrderPlaced", new { Number = "SO-1", Address = new { Country = "BG", Postcode = "1000" } });

        Assert.Equal("reminder:1:country=BG;postcode=1000", (await ScheduleAsync(catalog, @event)).Key);
    }

    [Theory]
    [InlineData("order-1001", "reminder:1:order-1001")]
    [InlineData(null, null)]
    public async Task ExecuteAsync_WithoutADeclaredSubject_UsesTheEnvelopesSubject(string? subject, string? key)
    {
        var catalog = new TestCatalog().Action("log", new NoOpAction());

        var scheduled = await ScheduleAsync(catalog, new RuleEvent("OrderPlaced", new { Number = "SO-1" }) { Subject = subject });

        Assert.Equal(key, scheduled.Key);
    }

    private static async Task<ScheduledAction> ScheduleAsync(TestCatalog catalog, RuleEvent @event)
    {
        var rule = new RuleDefinition
        {
            Id = "reminder", Name = "Reminder", EventType = "OrderPlaced", Condition = Op("eq", Value(1), Value(1)),
            Actions =
            [
                new ActionDefinition { Action = "log" },
                new ActionDefinition
                {
                    Action = "log",
                    Schedule = new ScheduleMetadata { Mode = ScheduleMode.Delay, Amount = 2, Unit = ScheduleUnit.Hours }
                }
            ]
        };

        var result = await catalog.Engine(new FixedTimeProvider(Due.AddHours(-2))).ExecuteAsync(@event, [rule], TestContext.Current.CancellationToken);
        return Assert.Single(result.ScheduledActions);
    }

    private static EventSchemaDefinition Schema(string subject) => EventSchemaDefinition.Parse($$"""
        { "x-ruleloom-event": "OrderPlaced", "type": "object",
          "properties": { "number": { "type": "string" },
            "address": { "type": "object", "properties": { "country": { "type": "string" }, "postcode": { "type": "string" } } } },
          "x-ruleloom-subject": {{subject}} }
        """);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoOpAction : IAction
    {
        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActionResult.Success());
    }
}
