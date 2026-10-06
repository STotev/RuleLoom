using RuleForge.Abstractions;
using RuleForge.Abstractions.Action;
using RuleForge.Actions;

namespace RuleForge.Tests;

public class DirectActionExecutorFactoryTests
{
    [Fact]
    public void Constructor_WithExecutors_RegistersEachUnderItsDefaultName()
    {
        var sendEmail = new SendEmailAction();
        var factory = new DirectActionExecutorFactory([sendEmail, new LogAction()]);

        Assert.True(factory.TryGetExecutor("send-email", out var executor));
        Assert.Same(sendEmail, executor);
        Assert.Equal(["log", "send-email"], factory.ActionNames.Order());
    }

    [Fact]
    public void Add_WithName_RegistersUnderThatName()
    {
        var sendEmail = new SendEmailAction();
        var factory = new DirectActionExecutorFactory().Add(sendEmail, "notify");

        Assert.True(factory.TryGetExecutor("notify", out var executor));
        Assert.Same(sendEmail, executor);
        Assert.False(factory.TryGetExecutor("send-email", out _));
    }

    [Fact]
    public void Add_SameExecutorUnderTwoNames_IsAllowed()
    {
        var sendEmail = new SendEmailAction();
        var factory = new DirectActionExecutorFactory().Add(sendEmail).Add(sendEmail, "notify");

        Assert.Equal(["notify", "send-email"], factory.ActionNames.Order());
    }

    [Fact]
    public void Add_AttributeName_IsUsedAsDefault()
    {
        var factory = new DirectActionExecutorFactory().Add(new RenamedAction());

        Assert.Equal(["sms"], factory.ActionNames);
    }

    [Theory]
    [InlineData("send-email")]
    [InlineData("Send-Email")]
    [InlineData("SEND-EMAIL")]
    public void TryGetExecutor_NameInAnyCase_ReturnsExecutor(string name)
    {
        var sendEmail = new SendEmailAction();
        var factory = new DirectActionExecutorFactory([sendEmail]);

        Assert.True(factory.TryGetExecutor(name, out var executor));
        Assert.Same(sendEmail, executor);
    }

    [Fact]
    public void TryGetExecutor_UnknownName_ReturnsFalse()
    {
        var factory = new DirectActionExecutorFactory([new SendEmailAction()]);

        Assert.False(factory.TryGetExecutor("unknown", out var executor));
        Assert.Null(executor);
    }

    [Fact]
    public void Constructor_NullExecutors_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DirectActionExecutorFactory(null!));
    }

    [Fact]
    public void Add_DuplicateName_ThrowsNamingIt()
    {
        var factory = new DirectActionExecutorFactory().Add(new SendEmailAction());

        var ex = Assert.Throws<ArgumentException>(() => factory.Add(new LogAction(), "send-email"));

        Assert.StartsWith("Action name 'send-email' is registered more than once.", ex.Message);
    }

    [Fact]
    public void Add_NameDifferingOnlyInCase_ThrowsNamingBoth()
    {
        var factory = new DirectActionExecutorFactory().Add(new LogAction());

        var ex = Assert.Throws<ArgumentException>(() => factory.Add(new SendEmailAction(), "Log"));

        Assert.StartsWith("Action name 'Log' conflicts with 'log' (names are case-insensitive).", ex.Message);
    }

    private sealed class SendEmailAction : IActionExecutor
    {
        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(ActionResult.Success());
    }

    [ActionName("sms")]
    private sealed class RenamedAction : IActionExecutor
    {
        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(ActionResult.Success());
    }
}
