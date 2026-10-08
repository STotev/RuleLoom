using System.Text.Json;
using System.Text.Json.Nodes;
using RuleForge.Abstractions.Values;

namespace RuleForge.Tests;

public class ValueComparerTests
{
    private static readonly DateTime NoonUtc = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public static TheoryData<object?, object?, bool> Equality => new()
    {
        // Numbers across types
        { 100m, 100, true },
        { 100, 100L, true },
        { (byte)5, 5m, true },
        { 5, 5.0, true },
        { 100m, 101, false },
        { 19.99m, 19.99, true },
        { 0.1m, 0.1, true },
        { 3_000_000_000L, 3_000_000_000m, true },
        { ulong.MaxValue, (decimal)ulong.MaxValue, true },
        { double.NaN, double.NaN, false },
        // Enums
        { Status.Active, "Active", true },
        { Status.Active, "active", true },
        { "ACTIVE", Status.Active, true },
        { Status.Active, "Inactive", false },
        { Status.Active, "Unknown", false },
        { Status.Active, "1", false },
        { Status.Active, 1, true },
        { 1L, Status.Active, true },
        { Status.Active, 2, false },
        { Status.Active, Status.Active, true },
        { Status.Active, OtherStatus.Active, false },
        { Access.Read | Access.Write, "Read, Write", true },
        { Access.Read | Access.Write, "read,write", true },
        // Dates
        { NoonUtc, new DateTimeOffset(NoonUtc), true },
        { NoonUtc, new DateTimeOffset(2026, 1, 1, 14, 0, 0, TimeSpan.FromHours(2)), true },
        { NoonUtc, DateTime.SpecifyKind(NoonUtc, DateTimeKind.Unspecified), true },
        { NoonUtc, NoonUtc.ToLocalTime(), true },
        { NoonUtc, "2026-01-01T12:00:00Z", true },
        { NoonUtc, "2026-01-01T12:00:00", true },
        { NoonUtc, "2026-01-01T14:00:00+02:00", true },
        { NoonUtc, "2026-01-01T12:00", true },
        { "2026-01-01T12:00:00.0000000Z", NoonUtc, true },
        { new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "2026-01-01", true },
        { NoonUtc, "2026-01-01", false },
        { NoonUtc, "01/01/2026 12:00", false },
        { NoonUtc, "1 January 2026", false },
        { NoonUtc, 1, false },
        // Strings
        { "Bob", "Bob", true },
        { "Bob", "bob", false },
        // ISO 8601 strings on both sides compare as instants; a date against other text never matches
        { "2026-01-01", "2026-01-01T00:00:00Z", true },
        { "2026-05-01T14:00:00+02:00", "2026-05-01T12:00:00Z", true },
        { "2026-05-01T12:00:00", "2026-05-01T12:00:00Z", true },
        { "2026-05-01T12:00:00Z", "2026-05-01T12:00:01Z", false },
        { "2026-05-01", "May 1st", false },
        { "2026-13-45", "2026-13-45", true },
        // Nulls
        { null, null, true },
        { null, 0, false },
        { "", null, false },
        // Fallback to Equals
        { true, true, true },
        { true, "true", false },
        { 1, "1", false },
        { Guid.Empty, Guid.Empty, true }
    };

    [Theory]
    [MemberData(nameof(Equality))]
    public void AreEqual_Values_FollowsComparisonRules(object? left, object? right, bool expected)
    {
        Assert.Equal(expected, ValueComparer.AreEqual(left, right));
        Assert.Equal(expected, ValueComparer.AreEqual(right, left));
    }

    public static TheoryData<object?, object?, int> Orderable => new()
    {
        { 1, 2m, -1 },
        { 2.5, 2, 1 },
        { 100m, 100L, 0 },
        { 1e30, 1m, 1 },
        { Status.Inactive, Status.Active, 1 },
        { Status.Active, "Inactive", -1 },
        { Status.Inactive, 1, 1 },
        { NoonUtc, "2026-01-01T13:00:00Z", -1 },
        { NoonUtc, new DateTimeOffset(NoonUtc).AddHours(-1), 1 },
        { "apple", "banana", -1 },
        { "B", "a", -1 },
        { "2026-05-01T14:00:00+02:00", "2026-05-01T12:30:00Z", -1 },
        { "2026-05-02", "2026-05-01T23:59:59Z", 1 },
        { "2026-13-45", "2026-12-99", 1 },
        { TimeSpan.FromHours(2), TimeSpan.FromHours(1), 1 },
        { false, true, -1 }
    };

    [Theory]
    [MemberData(nameof(Orderable))]
    public void TryCompare_ComparableValues_ReturnsOrdering(object? left, object? right, int expected)
    {
        Assert.True(ValueComparer.TryCompare(left, right, out var result));
        Assert.Equal(expected, result);

        Assert.True(ValueComparer.TryCompare(right, left, out var reversed));
        Assert.Equal(-expected, reversed);
    }

    public static TheoryData<object?, object?> NotOrderable => new()
    {
        { null, 1 },
        { null, null },
        { 1, "1" },
        { 5, "abc" },
        { double.NaN, 1 },
        { NoonUtc, "not a date" },
        { NoonUtc, 5 },
        { Status.Active, OtherStatus.Active },
        { Status.Active, "Unknown" },
        { TimeSpan.Zero, 0 },
        { new object(), new object() },
        { "2026-05-01", "May 1st" },
        { "2026-13-45", "2026-12-01" },
        { "2026-05-01T12:00:00Z", "abc" },
        { JsonDocument.Parse("{}").RootElement, JsonDocument.Parse("{}").RootElement }
    };

    [Theory]
    [MemberData(nameof(NotOrderable))]
    public void TryCompare_IncomparableValues_ReturnsFalse(object? left, object? right)
    {
        Assert.False(ValueComparer.TryCompare(left, right, out _));
        Assert.False(ValueComparer.TryCompare(right, left, out _));
    }

    [Fact]
    public void AreEqual_FloatAgainstDouble_ComparesAsDouble()
    {
        // 0.1f widened to double is 0.100000001…, so it doesn't equal the double 0.1 (documented precision rule).
        Assert.False(ValueComparer.AreEqual(0.1f, 0.1));
        Assert.True(ValueComparer.AreEqual(0.5f, 0.5));
    }

    public enum Status
    {
        Draft,
        Active,
        Inactive
    }

    public enum OtherStatus
    {
        Draft,
        Active
    }

    [Flags]
    public enum Access
    {
        None = 0,
        Read = 1,
        Write = 2
    }

    [Theory]
    [InlineData("""{ "a": 1, "b": [1, 2] }""", """{ "b": [1, 2], "a": 1 }""", true)]
    [InlineData("""{ "a": 1 }""", """{ "a": 2 }""", false)]
    [InlineData("""[1, 2]""", """[2, 1]""", false)]
    public void AreEqual_JsonContent_ComparesDeeply(string left, string right, bool expected)
    {
        Assert.Equal(expected, ValueComparer.AreEqual(JsonDocument.Parse(left).RootElement, JsonDocument.Parse(right).RootElement));
        Assert.Equal(expected, ValueComparer.AreEqual(JsonNode.Parse(left), JsonNode.Parse(right)));
    }

    [Fact]
    public void AreEqual_JsonElementAgainstJsonNode_IsFalse()
    {
        Assert.False(ValueComparer.AreEqual(JsonDocument.Parse("{}").RootElement, JsonNode.Parse("{}")));
        Assert.False(ValueComparer.AreEqual(JsonDocument.Parse("{}").RootElement, "{}"));
    }
}
