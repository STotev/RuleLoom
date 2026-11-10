using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Abstractions.Scheduling;
using RuleLoom.Abstractions.Values;
using RuleLoom.Schemas;
using static RuleLoom.Tests.Conditions;

namespace RuleLoom.Tests;

// The catalog's time zone, end to end. Europe/Sofia in 2026: +03:00 in summer; 03:00–04:00 skipped on 29 March.
public class TimeZoneTests
{
    [Fact]
    public void UseTimeZone_SetsTheCatalogsRules_AndTheDocumentNamesIt()
    {
        var catalog = new CatalogBuilder().UseTimeZone("Europe/Sofia").Build();
        var document = CatalogDocument.Parse(catalog.ToJson());

        Assert.Equal("Europe/Sofia", catalog.TemporalRules.TimeZone.Id);
        Assert.Equal("Europe/Sofia", document.TimeZone);
    }

    [Fact]
    public void Build_WithoutATimeZone_IsUtc()
    {
        var catalog = new CatalogBuilder().Build();

        Assert.Same(TemporalRules.Utc, catalog.TemporalRules);
        Assert.Equal("UTC", catalog.ToDocument().TimeZone);
    }

    [Fact]
    public void Build_UnknownTimeZone_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new CatalogBuilder().UseTimeZone("Mars/Olympus_Mons").Build());

        Assert.StartsWith("Time zone 'Mars/Olympus_Mons' isn't known on this machine", ex.Message);
    }

    [Fact]
    public void CatalogDocument_WithoutATimeZone_HasNone()
    {
        Assert.Null(CatalogDocument.Parse("{}").TimeZone);
        Assert.DoesNotContain("timeZone", new CatalogDocument().ToJson());
    }

    [Fact]
    public async Task ExecuteAsync_DateTimesWithoutAnOffset_AreReadInTheCatalogsTimeZone()
    {
        var engine = new TestCatalog().TimeZone("Europe/Sofia").Action("log", new LogAction()).Engine();
        var order = new { PlacedAt = new DateTime(2026, 7, 1, 9, 0, 0) };   // Unspecified: 06:00 UTC in Sofia
        var rule = Rule(Op("eq", Path("PlacedAt"), Value("2026-07-01T06:00:00Z")));

        var result = await engine.ExecuteAsync("Event", order, [rule], TestContext.Current.CancellationToken);

        Assert.Equal(RuleExecutionStatus.Matched, Assert.Single(result.Rules).Status);
    }

    [Fact]
    public async Task ExecuteAsync_ATimeTheClocksSkip_FailsTheRule_NamingThePath()
    {
        var engine = new TestCatalog().TimeZone("Europe/Sofia").Action("log", new LogAction()).Engine();
        var rule = Rule(Op("gt", Path("PlacedAt"), Value("2026-01-01T00:00:00Z")));

        var result = await engine.ExecuteAsync("Event", new { PlacedAt = "2026-03-29T03:30:00" }, [rule], TestContext.Current.CancellationToken);

        var failed = Assert.Single(result.Rules);
        Assert.Equal(RuleExecutionStatus.Failed, failed.Status);
        var error = Assert.IsType<InvalidTemporalValueException>(failed.Error);
        Assert.Equal("PlacedAt", error.Path);
        Assert.Equal("'PlacedAt' is '2026-03-29T03:30:00', which doesn't exist in time zone 'Europe/Sofia': the clocks skip that time.", error.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ScheduleFromAReferenceWithoutAnOffset_IsInTheTimeZone()
    {
        var engine = new TestCatalog().TimeZone("Europe/Sofia").Action("log", new LogAction()).Engine();
        var action = new ActionDefinition
        {
            Action = "log",
            Schedule = new ScheduleMetadata { Mode = ScheduleMode.Advance, Amount = 1, Unit = ScheduleUnit.Hours, ReferencePath = "DueAt" }
        };

        var result = await engine.ExecuteAsync("Event", new { DueAt = "2026-07-01T09:00" }, [Rule(Op("eq", Value(1), Value(1)), action)],
            TestContext.Current.CancellationToken);

        Assert.Equal(new DateTimeOffset(2026, 7, 1, 5, 0, 0, TimeSpan.Zero), Assert.Single(result.ScheduledActions).Result.ScheduledFor);
    }

    [Fact]
    public async Task ExecuteAsync_ScheduleFromATimeTheClocksSkip_FailsTheAction_NamingTheReferencePath()
    {
        var engine = new TestCatalog().TimeZone("Europe/Sofia").Action("log", new LogAction()).Engine();
        var action = new ActionDefinition
        {
            Action = "log",
            Schedule = new ScheduleMetadata { Mode = ScheduleMode.Delay, Amount = 1, Unit = ScheduleUnit.Hours, ReferencePath = "DueAt" }
        };

        var result = await engine.ExecuteAsync("Event", new { DueAt = "2026-03-29T03:30" }, [Rule(Op("eq", Value(1), Value(1)), action)],
            TestContext.Current.CancellationToken);

        var failed = Assert.Single(Assert.Single(result.Rules).ActionResults);
        Assert.Equal(ActionExecutionStatus.Failed, failed.Status);
        Assert.Equal("DueAt", Assert.IsType<InvalidTemporalValueException>(failed.Error).Path);
    }

    [Fact]
    public async Task GetParametersAsync_DateTimesWithoutAnOffset_AreReadInTheTimeZone()
    {
        var context = new TestCatalog().TimeZone("Europe/Sofia").Context(new { DueAt = new DateTime(2026, 7, 1, 9, 0, 0) }, null, "Event");
        var action = new ActionDefinition
        {
            Action = "remind",
            Parameters = { ["sendAt"] = "2026-07-01T10:00", ["dueAt"] = RulePath.Parse("DueAt"), ["day"] = "2026-07-01T10:00" }
        };

        var parameters = await action.GetParametersAsync<ReminderParameters>(context, TestContext.Current.CancellationToken);

        Assert.Equal(new DateTimeOffset(2026, 7, 1, 7, 0, 0, TimeSpan.Zero), parameters.SendAt);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 6, 0, 0, TimeSpan.Zero), parameters.DueAt);
        Assert.Equal(new DateTime(2026, 7, 1, 7, 0, 0, DateTimeKind.Utc), parameters.Day);
    }

    [Fact]
    public async Task GetParametersAsync_ATimeTheClocksSkip_NamesTheParameterOrItsPath()
    {
        var context = new TestCatalog().TimeZone("Europe/Sofia").Context(new { DueAt = "2026-03-29T03:30" }, null, "Event");
        var literal = new ActionDefinition { Action = "remind", Parameters = { ["sendAt"] = "2026-03-29T03:15", ["dueAt"] = "2026-07-01T10:00" } };
        var path = new ActionDefinition { Action = "remind", Parameters = { ["sendAt"] = "2026-07-01T10:00", ["dueAt"] = RulePath.Parse("DueAt") } };

        var fromLiteral = await Assert.ThrowsAsync<InvalidTemporalValueException>(() =>
            literal.GetParametersAsync<ReminderParameters>(context, TestContext.Current.CancellationToken).AsTask());
        var fromPath = await Assert.ThrowsAsync<InvalidTemporalValueException>(() =>
            path.GetParametersAsync<ReminderParameters>(context, TestContext.Current.CancellationToken).AsTask());

        Assert.StartsWith("Parameter 'sendAt' of action 'remind' is '2026-03-29T03:15', which doesn't exist", fromLiteral.Message);
        Assert.Equal("DueAt", fromPath.Path);
    }

    [Fact]
    public async Task GetParameterAsync_ReadsWithTheTimeZone_GetParameterWithUtc()
    {
        var context = new TestCatalog().TimeZone("Europe/Sofia").Context(new { }, null, "Event");
        var action = new ActionDefinition { Action = "remind", Parameters = { ["sendAt"] = "2026-07-01T10:00" } };

        Assert.Equal(new DateTimeOffset(2026, 7, 1, 7, 0, 0, TimeSpan.Zero),
            await action.GetParameterAsync<DateTimeOffset>("sendAt", context, TestContext.Current.CancellationToken));
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero), action.GetParameter<DateTimeOffset>("sendAt"));
    }

    public sealed record ReminderParameters(DateTimeOffset SendAt, DateTimeOffset DueAt, DateTime? Day = null);

    private static RuleDefinition Rule(Abstractions.Condition.ConditionNode condition, params ActionDefinition[] actions) =>
        new() { Id = "rule", Name = "Rule", EventType = "Event", Condition = condition, Actions = [.. actions] };

    private sealed class LogAction : IAction
    {
        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActionResult.Success());
    }
}
