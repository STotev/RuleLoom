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

        var ex = Assert.Throws<ArgumentException>(() => new DirectActionExecutorFactory(executors));

        Assert.StartsWith("Action type 'duplicate' is registered more than once.", ex.Message);
    }

    [Theory]
    [InlineData("send-email")]
    [InlineData("Send-Email")]
    [InlineData("SEND-EMAIL")]
    public void TryGetExecutor_TypeInAnyCase_ReturnsExecutor(string type)
    {
        var expectedExecutor = new StubActionExecutor("send-email");
        var factory = new DirectActionExecutorFactory([expectedExecutor]);

        Assert.True(factory.TryGetExecutor(type, out var executor));
        Assert.Same(expectedExecutor, executor);
    }

    [Fact]
    public void Constructor_TypesDifferingOnlyInCase_ThrowsNamingBoth()
    {
        var executors = new IActionExecutor[] { new StubActionExecutor("log"), new StubActionExecutor("Log") };

        var ex = Assert.Throws<ArgumentException>(() => new DirectActionExecutorFactory(executors));

        Assert.StartsWith("Action type 'Log' conflicts with 'log' (types are case-insensitive).", ex.Message);
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
