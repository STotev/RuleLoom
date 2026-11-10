using RuleLoom.Abstractions.Values;

namespace RuleLoom.Tests;

// Europe/Sofia in 2026: EET (+02:00) until 29 March 03:00, when clocks move to 04:00 (EEST, +03:00); back from 04:00 to
// 03:00 on 25 October.
public class TemporalRulesTests
{
    private static readonly TemporalRules Sofia = new(TimeZoneInfo.FindSystemTimeZoneById("Europe/Sofia"));

    [Theory]
    [InlineData("2026-07-01T09:00", "2026-07-01T06:00:00Z")]
    [InlineData("2026-07-01T09:00:00.5", "2026-07-01T06:00:00.5Z")]
    [InlineData("2026-01-15T09:00:00", "2026-01-15T07:00:00Z")]
    [InlineData("2026-05-01", "2026-04-30T21:00:00Z")]
    public void TryGetInstant_TextWithoutAnOffset_IsWallClockTimeInTheTimeZone(string text, string expected)
    {
        Assert.True(Sofia.TryGetInstant(text, out var instant));
        Assert.Equal(DateTimeOffset.Parse(expected), instant);
    }

    [Theory]
    [InlineData("2026-07-01T09:00:00+02:00", "2026-07-01T07:00:00Z")]
    [InlineData("2026-07-01T09:00:00Z", "2026-07-01T09:00:00Z")]
    public void TryGetInstant_AnOffset_Wins(string text, string expected)
    {
        Assert.True(Sofia.TryGetInstant(text, out var instant));
        Assert.Equal(DateTimeOffset.Parse(expected), instant);
    }

    [Fact]
    public void TryGetInstant_DateTimes_UnspecifiedInTheTimeZone_UtcAndOffsetsAsThey_Are()
    {
        var wallClock = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Unspecified);
        var utc = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);
        var offset = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.FromHours(-4));

        Assert.True(Sofia.TryGetInstant(wallClock, out var fromWallClock));
        Assert.True(Sofia.TryGetInstant(utc, out var fromUtc));
        Assert.True(Sofia.TryGetInstant(offset, out var fromOffset));

        Assert.Equal(new DateTimeOffset(2026, 7, 1, 6, 0, 0, TimeSpan.Zero), fromWallClock);
        Assert.Equal(new DateTimeOffset(utc), fromUtc);
        Assert.Equal(offset, fromOffset);
    }

    [Fact]
    public void TryGetInstant_Local_IsTheMachinesTimeZone()
    {
        var local = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Local);

        Assert.True(Sofia.TryGetInstant(local, out var instant));
        Assert.Equal(new DateTimeOffset(local.ToUniversalTime()), instant);
    }

    [Theory]
    [InlineData("2026-03-29T03:30")]
    [InlineData("2026-03-29T03:00:00")]
    public void TryGetInstant_WallClockTimeTheClocksSkip_Throws(string text)
    {
        var ex = Assert.Throws<InvalidTemporalValueException>(() => Sofia.TryGetInstant(text, out _));

        Assert.Equal((text, "Europe/Sofia", null), (ex.Value, ex.TimeZone, ex.Path));
        Assert.Equal($"'{text}' doesn't exist in time zone 'Europe/Sofia': the clocks skip that time.", ex.Message);
    }

    [Fact]
    public void TryGetInstant_UnspecifiedDateTimeTheClocksSkip_Throws()
    {
        var skipped = new DateTime(2026, 3, 29, 3, 30, 0);

        var ex = Assert.Throws<InvalidTemporalValueException>(() => Sofia.TryGetInstant(skipped, out _));

        Assert.Equal("2026-03-29T03:30:00", ex.Value);
    }

    [Fact]
    public void TryGetInstant_WallClockTimeThatOccursTwice_IsTheEarlierOne()
    {
        Assert.True(Sofia.TryGetInstant("2026-10-25T03:30", out var instant));

        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), instant);   // still EEST, +03:00
    }

    [Theory]
    [InlineData(true)]
    [InlineData(42)]
    [InlineData("hello")]
    [InlineData("2026-13-01")]
    [InlineData("01/05/2026")]
    [InlineData(null)]
    public void TryGetInstant_NotADateTime_ReturnsFalse(object? value)
    {
        Assert.False(Sofia.TryGetInstant(value, out _));
    }

    [Fact]
    public void Utc_ReadsTextWithoutAnOffsetAsUtc()
    {
        Assert.True(TemporalRules.Utc.TryGetInstant("2026-03-29T03:30", out var instant));

        Assert.Equal(new DateTimeOffset(2026, 3, 29, 3, 30, 0, TimeSpan.Zero), instant);
        Assert.Equal("UTC", TemporalRules.Utc.TimeZone.Id);
    }

    [Fact]
    public void ValueComparer_ComparesDateTimesAsMomentsInTheTimeZone()
    {
        var sixUtc = new DateTimeOffset(2026, 7, 1, 6, 0, 0, TimeSpan.Zero);

        Assert.True(ValueComparer.AreEqual("2026-07-01T09:00", sixUtc, Sofia));
        Assert.False(ValueComparer.AreEqual("2026-07-01T09:00", sixUtc, TemporalRules.Utc));
        Assert.True(ValueComparer.TryCompare(new DateTime(2026, 7, 1, 9, 30, 0), sixUtc, Sofia, out var order));
        Assert.Equal(1, order);
    }

    [Fact]
    public void ValueConverter_ReadsDateTimesWithTheRules()
    {
        Assert.True(ValueConverter.TryConvert<DateTime>("2026-07-01T09:00", Sofia, out var converted));

        Assert.Equal(new DateTime(2026, 7, 1, 6, 0, 0, DateTimeKind.Utc), converted);
        Assert.Equal(DateTimeKind.Utc, converted.Kind);
    }
}
