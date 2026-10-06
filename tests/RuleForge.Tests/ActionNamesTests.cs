using RuleForge.Abstractions;
using RuleForge.Abstractions.Action;
using RuleForge.Actions;

namespace RuleForge.Tests;

public class ActionNamesTests
{
    [Theory]
    [InlineData(typeof(SendEmailAction), "send-email")]
    [InlineData(typeof(LogAction), "log")]
    [InlineData(typeof(HttpRequestAction), "http-request")]
    [InlineData(typeof(SMSAction), "sms")]
    [InlineData(typeof(Notifier), "notifier")]
    [InlineData(typeof(Action), "action")]
    [InlineData(typeof(TransactionAction), "transaction")]
    [InlineData(typeof(GenericAction<int>), "generic")]
    [InlineData(typeof(AttributedAction), "notify")]
    public void GetDefaultName_ActionType_FollowsConvention(Type type, string expected)
    {
        Assert.Equal(expected, ActionNames.GetDefaultName(type));
    }

    [Fact]
    public void GetDefaultName_AttributeIsNotInherited()
    {
        Assert.Equal("derived-attributed", ActionNames.GetDefaultName(typeof(DerivedAttributedAction)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ActionNameAttribute_EmptyName_Throws(string name)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ActionNameAttribute(name));
    }

    private abstract class NoOp : IAction
    {
        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(ActionResult.Success());
    }

    private sealed class SendEmailAction : NoOp;
    private sealed class HttpRequestAction : NoOp;
    private sealed class SMSAction : NoOp;
    private sealed class Notifier : NoOp;
    private sealed class Action : NoOp;
    private sealed class TransactionAction : NoOp;
    private sealed class GenericAction<T> : NoOp;

    [ActionName("notify")]
    private class AttributedAction : NoOp;

    private sealed class DerivedAttributedAction : AttributedAction;
}
