using RuleForge.Abstractions.Values;

namespace RuleForge.Tests;

public class ValueConverterTests
{
    public static TheoryData<object?, Type, object?> Convertible => new()
    {
        // Identity and null
        { "text", typeof(string), "text" },
        { 5, typeof(object), 5 },
        { null, typeof(string), null },
        { null, typeof(int?), null },
        // Lossless numeric conversions
        { 100, typeof(decimal), 100m },
        { 100L, typeof(int), 100 },
        { 5.0, typeof(int), 5 },
        { 0.1, typeof(decimal), 0.1m },
        { 19.99m, typeof(double), 19.99 },
        { 0.5f, typeof(double), 0.5 },
        { 100, typeof(int?), 100 },
        { 255, typeof(byte), (byte)255 },
        // Enums
        { "Active", typeof(ValueComparerTests.Status), ValueComparerTests.Status.Active },
        { "inactive", typeof(ValueComparerTests.Status), ValueComparerTests.Status.Inactive },
        { 1, typeof(ValueComparerTests.Status), ValueComparerTests.Status.Active },
        { 2L, typeof(ValueComparerTests.Status?), ValueComparerTests.Status.Inactive },
        { "Read, Write", typeof(ValueComparerTests.Access), ValueComparerTests.Access.Read | ValueComparerTests.Access.Write }
    };

    [Theory]
    [MemberData(nameof(Convertible))]
    public void TryConvert_ConvertibleValue_ReturnsConvertedValue(object? value, Type target, object? expected)
    {
        Assert.True(ValueConverter.TryConvert(value, target, out var result));
        Assert.Equal(expected, result);
    }

    public static TheoryData<object?, Type> NotConvertible => new()
    {
        { null, typeof(int) },
        { 5.5, typeof(int) },
        { 300, typeof(byte) },
        { -1, typeof(uint) },
        { long.MaxValue, typeof(int) },
        { double.NaN, typeof(int) },
        { 9_007_199_254_740_993L, typeof(double) },
        { "100", typeof(int) },
        { 100, typeof(string) },
        { "Unknown", typeof(ValueComparerTests.Status) },
        { "1", typeof(ValueComparerTests.Status) },
        { " 1", typeof(ValueComparerTests.Status) },
        { 1.5, typeof(ValueComparerTests.Status) },
        { "01/02/2026", typeof(DateTimeOffset) },
        { "2026-01-01 12:00", typeof(DateTime) },
        { 5, typeof(DateTime) },
        { "true", typeof(bool) }
    };

    [Theory]
    [MemberData(nameof(NotConvertible))]
    public void TryConvert_UnsupportedValue_ReturnsFalse(object? value, Type target)
    {
        Assert.False(ValueConverter.TryConvert(value, target, out _));
    }

    [Theory]
    [InlineData("2026-05-01T12:00:00Z", 12, 0)]
    [InlineData("2026-05-01T12:00:00", 12, 0)]
    [InlineData("2026-05-01T14:30:00+02:00", 12, 30)]
    [InlineData("2026-05-01T12:00:00.1234567Z", 12, 0)]
    [InlineData("2026-05-01", 0, 0)]
    public void TryConvert_IsoString_ReturnsUtcDateTimeOffset(string text, int hour, int minute)
    {
        Assert.True(ValueConverter.TryConvert<DateTimeOffset>(text, out var result));

        Assert.Equal(TimeSpan.Zero, result.Offset);
        Assert.Equal(new DateTime(2026, 5, 1), result.UtcDateTime.Date);
        Assert.Equal(hour, result.UtcDateTime.Hour);
        Assert.Equal(minute, result.UtcDateTime.Minute);
    }

    [Fact]
    public void TryConvert_IsoStringToDateTime_ReturnsUtcKind()
    {
        Assert.True(ValueConverter.TryConvert<DateTime>("2026-05-01T14:00:00+02:00", out var result));

        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void TryConvert_DateTimeOffsetToDateTime_ReturnsSameInstantInUtc()
    {
        var source = new DateTimeOffset(2026, 5, 1, 14, 0, 0, TimeSpan.FromHours(2));

        Assert.True(ValueConverter.TryConvert<DateTime>(source, out var result));

        Assert.Equal(source.UtcDateTime, result);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void TryConvert_NullTargetType_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ValueConverter.TryConvert(1, null!, out _));
    }
}
