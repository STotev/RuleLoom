using RuleForge.Abstractions;
using RuleForge.Abstractions.Action;
using RuleForge.Actions;

namespace RuleForge.Tests;

public class DirectActionExecutorFactoryTests
{
    [Fact]
    public void TryGetExecutor_RegisteredType_ReturnsTrueAndExecutor()
    {
        var expectedExecutor = new StubActionExecutor("test-action");
        var factory = new DirectActionExecutorFactory([expectedExecutor]);

        var found = factory.TryGetExecutor("test-action", out var executor);

        Assert.True(found);
        Assert.Same(expectedExecutor, executor);
    }

    [Fact]
    public void TryGetExecutor_UnknownType_ReturnsFalse()
    {
        var factory = new DirectActionExecutorFactory([new StubActionExecutor("known-action")]);

        var found = factory.TryGetExecutor("unknown-action", out var executor);

        Assert.False(found);
        Assert.Null(executor);
    }

    [Fact]
    public void Constructor_NullExecutors_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DirectActionExecutorFactory(null!));
    }

    [Fact]
    public void Constructor_DuplicateTypes_ThrowsArgumentException()
    {
        var executors = new IActionExecutor[]
        {
            new StubActionExecutor("duplicate"),
            new StubActionExecutor("duplicate")
        };

        Assert.Throws<ArgumentException>(() => new DirectActionExecutorFactory(executors));
    }

    private sealed class StubActionExecutor(string type) : IActionExecutor
    {
        public string Type => type;

        public Task<ActionResult> ExecuteAsync(
            ActionDefinition action,
            EvaluationContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ActionResult.Success());
    }
}
