using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Abstractions.Values;
using RuleLoom.Schemas;
using RuleLoom.Validation;
using static RuleLoom.Tests.Conditions;

namespace RuleLoom.Tests;

// Dates are calendar days. Europe/Sofia is UTC+3 in summer, so 22:30 UTC on 30 April is already 1 May there.
public class CalendarDateTests
{
    private static readonly TemporalRules Sofia = new(TimeZoneInfo.FindSystemTimeZoneById("Europe/Sofia"));

    [Theory]
    [InlineData("2026-05-01", true)]
    [InlineData("2026-05-01T00:00", false)]
    [InlineData("2026-05-01T00:00:00Z", false)]
    [InlineData("01/05/2026", false)]
    public void TryGetDate_OnlyDatesAreDates(string text, bool isDate)
    {
        Assert.Equal(isDate, Sofia.TryGetDate(text, out _));
        Assert.True(Sofia.TryGetDate(new DateOnly(2026, 5, 1), out _));
        Assert.False(Sofia.TryGetDate(new DateTime(2026, 5, 1), out _));
    }

    [Fact]
    public void GetDate_IsTheCalendarDayInTheTimeZone_GetStartOfDayItsFirstMoment()
    {
        Assert.Equal(new DateOnly(2026, 5, 1), Sofia.GetDate(new DateTimeOffset(2026, 4, 30, 22, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new DateTimeOffset(2026, 4, 30, 21, 0, 0, TimeSpan.Zero), Sofia.GetStartOfDay(new DateOnly(2026, 5, 1)));
    }

    [Theory]
    [InlineData("2026-05-01", "2026-05-01", 0)]
    [InlineData("2026-05-02", "2026-05-01", 1)]
    [InlineData("2026-05-01T23:30", "2026-05-01", 0)]          // wall-clock time in Sofia: still 1 May
    [InlineData("2026-04-30T22:30:00Z", "2026-05-01", 0)]      // 01:30 on 1 May in Sofia
    [InlineData("2026-05-01T18:00", "2026-05-02", -1)]
    [InlineData("2026-05-02T00:10", "2026-05-01", 1)]          // "after 1 May"
    public void TryCompare_DatesByDay_DateTimesByTheirCalendarDay(string left, string right, int expected)
    {
        Assert.True(ValueComparer.TryCompare(left, right, Sofia, out var result));
        Assert.Equal(expected, Math.Sign(result));
        Assert.True(ValueComparer.TryCompare(right, left, Sofia, out var reversed));
        Assert.Equal(-expected, Math.Sign(reversed));
    }

    [Fact]
    public void AreEqual_DateOnlyWithTextAndDateTimes()
    {
        var day = new DateOnly(2026, 5, 1);

        Assert.True(ValueComparer.AreEqual(day, "2026-05-01", Sofia));
        Assert.True(ValueComparer.AreEqual(day, new DateTimeOffset(2026, 4, 30, 22, 30, 0, TimeSpan.Zero), Sofia));
        Assert.False(ValueComparer.AreEqual(day, new DateTimeOffset(2026, 4, 30, 22, 30, 0, TimeSpan.Zero), TemporalRules.Utc));
        Assert.False(ValueComparer.AreEqual(day, "1 May", Sofia));
    }

    [Fact]
    public void TryConvert_ToDateOnly_FromDatesAndDateTimes_AndBack()
    {
        Assert.True(ValueConverter.TryConvert<DateOnly>("2026-04-30T22:30:00Z", Sofia, out var date));
        Assert.Equal(new DateOnly(2026, 5, 1), date);
        Assert.True(ValueConverter.TryConvert<DateOnly>("2026-05-01", Sofia, out var text));
        Assert.Equal(new DateOnly(2026, 5, 1), text);
        Assert.True(ValueConverter.TryConvert<DateTimeOffset>(new DateOnly(2026, 5, 1), Sofia, out var start));
        Assert.Equal(new DateTimeOffset(2026, 4, 30, 21, 0, 0, TimeSpan.Zero), start);
    }

    [Fact]
    public async Task ExecuteAsync_PlacedOnADay_InTheCatalogsTimeZone()
    {
        var engine = new TestCatalog().TimeZone("Europe/Sofia").Engine();
        var rule = new RuleDefinition { Id = "may-day", Name = "May day", EventType = "OrderPlaced", Condition = Op("eq", Path("PlacedAt"), Value("2026-05-01")) };

        var result = await engine.ExecuteAsync("OrderPlaced", new { PlacedAt = new DateTimeOffset(2026, 4, 30, 22, 30, 0, TimeSpan.Zero) }, [rule],
            TestContext.Current.CancellationToken);

        Assert.Equal(RuleExecutionStatus.Matched, Assert.Single(result.Rules).Status);
    }

    [Fact]
    public void Validate_DateTimeLiteralTheClocksSkip_IsAnErrorAtTheLiteral()
    {
        var validator = new RuleValidator(Catalog().Build());
        var rule = Rule(Op("gt", Path("PlacedAt"), Value("2026-03-29T03:30")));

        var error = Assert.Single(validator.Validate(rule).Errors);

        Assert.Equal(new RuleValidationError("$.condition.operands[1]",
            "'2026-03-29T03:30' doesn't exist in time zone 'Europe/Sofia': the clocks skip that time."), error);
    }

    [Theory]
    [InlineData("2026-03-29T04:30")]   // after the gap
    [InlineData("2026-03-29")]         // a date
    [InlineData("2026-03-29T03:30Z")]  // an offset: a moment, whatever the zone
    public void Validate_ValidDateLiterals_AreFine(string value)
    {
        var result = new RuleValidator(Catalog().Build()).Validate(Rule(Op("gt", Path("PlacedAt"), Value(value))));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_DateTimeParameterTheClocksSkip_IsAnError()
    {
        var validator = new RuleValidator(Catalog().Build());
        var rule = Rule(Op("eq", Value(1), Value(1)), new ActionDefinition { Action = "remind", Parameters = { ["sendAt"] = "2026-03-29T03:30" } });

        var error = Assert.Single(validator.Validate(rule).Errors);

        Assert.Equal(new RuleValidationError("$.actions[0].parameters.sendAt",
            "'sendAt': '2026-03-29T03:30' doesn't exist in time zone 'Europe/Sofia': the clocks skip that time."), error);
    }

    private static CatalogBuilder Catalog() => new CatalogBuilder()
        .UseTimeZone("Europe/Sofia")
        .AddEvent<OrderPlacedSchema>()
        .AddAction<RemindAction>("remind");

    private static RuleDefinition Rule(Abstractions.Condition.ConditionNode condition, params ActionDefinition[] actions) =>
        new() { Id = "r", Name = "R", EventType = "OrderPlaced", Condition = condition, Actions = [.. actions] };

    public sealed class Order
    {
        public DateTimeOffset PlacedAt { get; set; }
    }

    public sealed class OrderPlacedSchema : EventSchema<Order>
    {
        public OrderPlacedSchema() => Event("OrderPlaced");
    }

    public sealed record RemindParameters(DateTimeOffset SendAt);

    public sealed class RemindAction : IAction<RemindParameters>
    {
        public Task<ActionResult> ExecuteAsync(RemindParameters parameters, ActionDefinition action, EvaluationContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(ActionResult.Success());
    }
}
