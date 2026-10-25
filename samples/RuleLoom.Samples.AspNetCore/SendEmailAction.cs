using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;

namespace RuleLoom.Samples.AspNetCore;

/// <summary>
/// Registered as "send-email". Pretends to send an email by logging it. Parameters: <c>to</c>, a path to the
/// recipient's address in the state (e.g. <c>Customer.Email</c>), and <c>subject</c>.
/// </summary>
public sealed class SendEmailAction(ILogger<SendEmailAction> logger) : IAction
{
    public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default)
    {
        var toPath = action.GetParameter<string>("to");
        var subject = action.GetParameter<string>("subject");

        if (context.Resolve(toPath) is not string to)
            return Task.FromResult(ActionResult.Failed(new InvalidOperationException($"No email address at '{toPath}'.")));

        logger.LogInformation("Email to {To}: {Subject}", to, subject);

        return Task.FromResult(ActionResult.Success($"Email sent to {to}."));
    }
}

public sealed class SendEmailDescriptor : ActionDescriptor<SendEmailAction>
{
    public SendEmailDescriptor()
    {
        Label("Send email");
        Description("Emails the address at the path in 'to' with the 'subject'.");
    }
}
