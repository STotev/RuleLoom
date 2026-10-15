using RuleLoom.Abstractions;

namespace RuleLoom.Tests;

public class PathResolverTests
{
    [Fact]
    public void Resolve_SamePathFromDifferentContexts_UsesSharedCache()
    {
        var first = new EvaluationContext(new SharedCacheProbe { Price = 100 });
        var second = new EvaluationContext(new SharedCacheProbe { Price = 200 });

        Assert.Equal(100, first.Resolve("Price"));
        Assert.True(PathResolver.Shared.IsCached(typeof(SharedCacheProbe), "Price"));
        Assert.Equal(200, second.Resolve("Price"));
    }

    [Fact]
    public void Resolve_MissingProperty_IsCached()
    {
        var resolver = new PathResolver();

        Assert.Null(resolver.Resolve(new SharedCacheProbe(), "Missing"));
        Assert.True(resolver.IsCached(typeof(SharedCacheProbe), "Missing"));
        Assert.Null(resolver.Resolve(new SharedCacheProbe(), "Missing"));
    }

    [Fact]
    public void Resolve_SamePathOnDifferentTypes_ResolvesEachType()
    {
        var resolver = new PathResolver();

        Assert.Equal(100, resolver.Resolve(new SharedCacheProbe { Price = 100 }, "Price"));
        Assert.Equal(1.5m, resolver.Resolve(new { Price = 1.5m }, "Price"));
    }

    [Fact]
    public void Resolve_ConcurrentCalls_ReturnCorrectValues()
    {
        var resolver = new PathResolver();
        var paths = Enumerable.Range(0, 50).Select(i => $"Item{i % 5}.Value").ToArray();
        var root = new ConcurrencyRoot();

        Parallel.For(0, 10_000, i =>
        {
            var path = paths[i % paths.Length];
            var expected = int.Parse(path[4..5]);

            Assert.Equal(expected, resolver.Resolve(root, path));
        });
    }

    [Theory]
    [InlineData("Customer.Name")]
    [InlineData("customer.name")]
    [InlineData("CUSTOMER.NAME")]
    [InlineData("$current.customer.name")]
    public void Resolve_PathInAnyCase_ResolvesProperty(string path)
    {
        var context = new EvaluationContext(new Order { Customer = new Customer { Name = "Ada" } });

        Assert.Equal("Ada", context.Resolve(path));
    }

    [Theory]
    [InlineData("Price", 1)]
    [InlineData("price", 2)]
    public void Resolve_PropertiesDifferingOnlyInCase_ExactCaseWins(string path, int expected)
    {
        Assert.Equal(expected, new PathResolver().Resolve(new CaseSensitiveProperties(), path));
    }

    [Fact]
    public void Resolve_AmbiguousCaseInsensitiveMatch_ReturnsNull()
    {
        Assert.Null(new PathResolver().Resolve(new CaseSensitiveProperties(), "PRICE"));
    }

    [Fact]
    public void Resolve_IndexerName_IsNotAProperty()
    {
        Assert.Null(new PathResolver().Resolve(new WithIndexer(), "item"));
        Assert.Null(new PathResolver().Resolve(new WithIndexer(), "Item"));
    }

    private sealed class Order
    {
        public Customer Customer { get; init; } = new();
    }

    private sealed class Customer
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class CaseSensitiveProperties
    {
        public int Price => 1;
        public int price => 2;
    }

    private sealed class WithIndexer
    {
        public int this[int index] => index;
    }

    private sealed class SharedCacheProbe
    {
        public int Price { get; init; }
    }

    private sealed class ConcurrencyRoot
    {
        public Leaf Item0 { get; } = new(0);
        public Leaf Item1 { get; } = new(1);
        public Leaf Item2 { get; } = new(2);
        public Leaf Item3 { get; } = new(3);
        public Leaf Item4 { get; } = new(4);
    }

    private sealed record Leaf(int Value);
}
