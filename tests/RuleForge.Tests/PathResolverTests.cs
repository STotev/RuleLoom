using RuleForge.Abstractions;

namespace RuleForge.Tests;

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
