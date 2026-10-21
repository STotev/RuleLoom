using RuleLoom.Abstractions;

namespace RuleLoom.Tests;

public class RulePathTests
{
    [Theory]
    [InlineData("Price", PathRoot.Current, null, "Price")]
    [InlineData("Address.City", PathRoot.Current, null, "Address.City")]
    [InlineData("$current.Price", PathRoot.Current, null, "Price")]
    [InlineData("$CURRENT", PathRoot.Current, null, "")]
    [InlineData("$previous.Address.City", PathRoot.Previous, null, "Address.City")]
    [InlineData("$Previous", PathRoot.Previous, null, "")]
    [InlineData("@customer", PathRoot.Context, "customer", "")]
    [InlineData("@customer.Address.City", PathRoot.Context, "customer", "Address.City")]
    [InlineData("current.Price", PathRoot.Current, null, "current.Price")]   // a plain property name
    public void Parse_ValidPaths_SplitRootAndProperty(string text, PathRoot root, string? context, string property)
    {
        var path = RulePath.Parse(text);

        Assert.Equal((root, context, property), (path.Root, path.Context, path.Property));
        Assert.Equal(property.Length == 0 ? [] : property.Split('.'), path.Segments);
    }

    [Theory]
    [InlineData("#now", "Paths starting with '#' are reserved for functions: '#now'.")]
    [InlineData("@", "A context path needs a name after '@', e.g. '@customer.Tier': '@'.")]
    [InlineData("@.Tier", "A context path needs a name after '@', e.g. '@customer.Tier': '@.Tier'.")]
    [InlineData("$now.Price", "Unknown variable '$now' in path '$now.Price'. Use $current or $previous.")]
    [InlineData("$current.@customer.Tier", "Contexts have no current or previous state; use '@customer.Tier' instead of '$current.@customer.Tier'.")]
    public void TryParse_InvalidPaths_ReturnTheReason(string text, string message)
    {
        Assert.False(RulePath.TryParse(text, out var path, out var error));
        Assert.Null(path);
        Assert.Equal(message, error);
        Assert.Equal(message, Assert.Throws<FormatException>(() => RulePath.Parse(text)).Message);
    }

    [Fact]
    public void InState_SwitchesTheStateOfTheSameProperty()
    {
        var path = RulePath.Parse("$current.Price");

        Assert.Equal(RulePath.Parse("$previous.Price"), path.InState(PathRoot.Previous));
        Assert.Same(path, path.InState(PathRoot.Current));
        Assert.Throws<InvalidOperationException>(() => RulePath.Parse("@customer.Tier").InState(PathRoot.Previous));
        Assert.Throws<ArgumentException>(() => path.InState(PathRoot.Context));
    }
}
