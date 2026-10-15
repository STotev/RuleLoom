using RuleLoom.Abstractions;

namespace RuleLoom.Tests;

public class EvaluationContextTests
{
    private static readonly Person CurrentPerson = new()
    {
        Name = "John",
        Address = new Address { City = "New York" }
    };

    private static readonly Person PreviousPerson = new()
    {
        Name = "Johnny",
        Address = new Address { City = "Boston" }
    };

    [Theory]
    [InlineData("Name", "John")]
    [InlineData("Address.City", "New York")]
    [InlineData("$current.Name", "John")]
    [InlineData("$current.Address.City", "New York")]
    [InlineData("$previous.Name", "Johnny")]
    [InlineData("$previous.Address.City", "Boston")]
    public void Resolve_Path_ReturnsValueFromExpectedScope(string path, string expected)
    {
        var context = new EvaluationContext(CurrentPerson, PreviousPerson);

        var result = context.Resolve(path);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("$Current.Address.City", "New York")]
    [InlineData("$PREVIOUS.Address.City", "Boston")]
    public void Resolve_ScopePrefixInAnyCase_SelectsScope(string path, string expected)
    {
        var context = new EvaluationContext(CurrentPerson, PreviousPerson);

        var result = context.Resolve(path);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Resolve_UnscopedNestedPath_MatchesExplicitCurrentScope()
    {
        var context = new EvaluationContext(CurrentPerson, PreviousPerson);

        Assert.Equal(context.Resolve("$current.Address.City"), context.Resolve("Address.City"));
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Missing.City")]
    [InlineData("Address.Missing")]
    public void Resolve_MissingProperty_ReturnsNull(string path)
    {
        var context = new EvaluationContext(CurrentPerson, PreviousPerson);

        Assert.Null(context.Resolve(path));
    }

    [Fact]
    public void Resolve_NullIntermediateProperty_ReturnsNull()
    {
        var context = new EvaluationContext(new Person { Name = "John", Address = null });

        Assert.Null(context.Resolve("Address.City"));
    }

    [Fact]
    public void Resolve_PreviousScopeWithoutPreviousState_ReturnsNull()
    {
        var context = new EvaluationContext(CurrentPerson);

        Assert.Null(context.Resolve("$previous.Address.City"));
    }

    [Fact]
    public void Resolve_PropertyNamedLikeScope_ResolvesWithPlainPath()
    {
        var context = new EvaluationContext(new { Previous = new { Price = 90 } }, new { Price = 80 });

        Assert.Equal(90, context.Resolve("Previous.Price"));
        Assert.Equal(80, context.Resolve("$previous.Price"));
    }

    [Fact]
    public void Resolve_PrefixWithoutDollar_IsAnOrdinaryPropertyName()
    {
        var context = new EvaluationContext(CurrentPerson, PreviousPerson);

        Assert.Null(context.Resolve("previous.Name"));
        Assert.Null(context.Resolve("current.Name"));
    }

    [Theory]
    [InlineData("$current", true)]
    [InlineData("$previous", false)]
    public void Resolve_VariableAlone_ReturnsWholeState(string path, bool isCurrent)
    {
        var context = new EvaluationContext(CurrentPerson, PreviousPerson);

        Assert.Same(isCurrent ? CurrentPerson : PreviousPerson, context.Resolve(path));
    }

    [Theory]
    [InlineData("$now")]
    [InlineData("$event.Type")]
    [InlineData("$cur.Name")]
    public void Resolve_UnknownVariable_Throws(string path)
    {
        var context = new EvaluationContext(CurrentPerson, PreviousPerson);

        var ex = Assert.Throws<InvalidOperationException>(() => context.Resolve(path));
        Assert.Contains("Use $current or $previous", ex.Message);
    }

    private sealed class Person
    {
        public string Name { get; init; } = string.Empty;
        public Address? Address { get; init; }
    }

    private sealed class Address
    {
        public string City { get; init; } = string.Empty;
    }
}
