using RuleForge.Abstractions;
using RuleForge.Abstractions.Action;
using RuleForge.Actions;

namespace RuleForge.Tests;

public class DirectActionFactoryTests
{
    [Fact]
    public void Constructor_WithExecutors_RegistersEachUnderItsDefaultName()
    {
        var sendEmail = new SendEmailAction();
        var factory = new DirectActionFactory([sendEmail, new ConsoleLogAction()]);

        Assert.True(factory.TryGetAction("send-email", out var executor));
        Assert.Same(sendEmail, executor);
        Assert.Equal(["console-log", "send-email"], factory.ActionNames.Order());
    }

    [Fact]
    public void Add_WithName_RegistersUnderThatName()
    {
        var sendEmail = new SendEmailAction();
        var factory = new DirectActionFactory().Add(sendEmail, "notify");

        Assert.True(factory.TryGetAction("notify", out var executor));
        Assert.Same(sendEmail, executor);
        Assert.False(factory.TryGetAction("send-email", out _));
    }

    [Fact]
    public void Add_SameExecutorUnderTwoNames_IsAllowed()
    {
        var sendEmail = new SendEmailAction();
        var factory = new DirectActionFactory().Add(sendEmail).Add(sendEmail, "notify");

        Assert.Equal(["notify", "send-email"], factory.ActionNames.Order());
    }

    [Fact]
    public void Add_AttributeName_IsUsedAsDefault()
    {
        var factory = new DirectActionFactory().Add(new RenamedAction());

        Assert.Equal(["sms"], factory.ActionNames);
    }

    [Theory]
    [InlineData("send-email")]
    [InlineData("Send-Email")]
    [InlineData("SEND-EMAIL")]
    public void TryGetAction_NameInAnyCase_ReturnsExecutor(string name)
    {
        var sendEmail = new SendEmailAction();
        var factory = new DirectActionFactory([sendEmail]);

        Assert.True(factory.TryGetAction(name, out var executor));
        Assert.Same(sendEmail, executor);
    }

    [Fact]
    public void TryGetAction_UnknownName_ReturnsFalse()
    {
        var factory = new DirectActionFactory([new SendEmailAction()]);

        Assert.False(factory.TryGetAction("unknown", out var executor));
        Assert.Null(executor);
    }

    [Fact]
    public void Constructor_NullExecutors_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DirectActionFactory(null!));
    }

    [Fact]
    public void Add_DuplicateName_ThrowsNamingIt()
    {
        var factory = new DirectActionFactory().Add(new SendEmailAction());

        var ex = Assert.Throws<ArgumentException>(() => factory.Add(new ConsoleLogAction(), "send-email"));

        Assert.StartsWith("Action name 'send-email' is registered more than once.", ex.Message);
    }

    [Fact]
    public void Add_NameDifferingOnlyInCase_ThrowsNamingBoth()
    {
        var factory = new DirectActionFactory().Add(new ConsoleLogAction());

        var ex = Assert.Throws<ArgumentException>(() => factory.Add(new SendEmailAction(), "Console-Log"));

        Assert.StartsWith("Action name 'Console-Log' conflicts with 'console-log' (names are case-insensitive).", ex.Message);
    }

    private sealed class SendEmailAction : IAction
    {
        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(ActionResult.Success());
    }

    [ActionName("sms")]
    private sealed class RenamedAction : IAction
    {
        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(ActionResult.Success());
    }
}
