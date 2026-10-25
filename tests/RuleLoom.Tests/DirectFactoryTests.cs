using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Context;
using RuleLoom.Actions;
using RuleLoom.Contexts;

namespace RuleLoom.Tests;

public class DirectFactoryTests
{
    [Fact]
    public void ActionFactory_AddedInstance_IsReturnedForEveryRegistrationOfItsType()
    {
        var sendEmail = new SendEmailAction();
        var factory = new DirectActionFactory([sendEmail, new ConsoleLogAction()]);

        Assert.Same(sendEmail, factory.Create(new ActionRegistration("send-email", typeof(SendEmailAction))));
        Assert.Same(sendEmail, factory.Create(new ActionRegistration("notify", typeof(SendEmailAction))));
    }

    [Fact]
    public void ActionFactory_WithoutAnInstance_CreatesTheTypeEachTime()
    {
        var factory = new DirectActionFactory();
        var registration = new ActionRegistration("send-email", typeof(SendEmailAction));

        var first = factory.Create(registration);

        Assert.IsType<SendEmailAction>(first);
        Assert.NotSame(first, factory.Create(registration));
    }

    [Fact]
    public void ActionFactory_WithoutAParameterlessConstructor_ThrowsNamingTheAction()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => new DirectActionFactory().Create(new ActionRegistration("mail", typeof(MailerAction))));

        Assert.Equal("Can't create MailerAction for 'mail': add an instance to the factory, or give it a public parameterless constructor.",
            error.Message);
    }

    [Fact]
    public void ActionFactory_SameTypeAddedTwice_Throws()
    {
        var factory = new DirectActionFactory().Add(new SendEmailAction());

        Assert.Throws<ArgumentException>(() => factory.Add(new SendEmailAction()));
        Assert.Throws<ArgumentNullException>(() => new DirectActionFactory(null!));
    }

    [Fact]
    public void ContextProviderFactory_ReturnsAddedInstancesAndCreatesOthers()
    {
        var customers = new FakeContextProvider(new { });
        var factory = new DirectContextProviderFactory().Add(customers);

        Assert.Same(customers, factory.Create(new ContextProviderRegistration("customer", typeof(FakeContextProvider))));
        Assert.IsType<SettingsProvider>(factory.Create(new ContextProviderRegistration("settings", typeof(SettingsProvider))));
        Assert.Throws<ArgumentException>(() => factory.Add(new FakeContextProvider(new { })));
    }

    public sealed class SendEmailAction : IAction
    {
        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(ActionResult.Success());
    }

    public sealed class MailerAction(string apiKey) : IAction
    {
        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(ActionResult.Success(apiKey));
    }

    public sealed class SettingsProvider : IContextProvider
    {
        public ValueTask<object?> GetAsync(ContextRequest request, CancellationToken cancellationToken) => ValueTask.FromResult<object?>(null);
    }
}
