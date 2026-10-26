using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;

namespace RuleLoom.Samples.AspNetCore;

/// <summary>The parameters of <c>send-email</c>; rules usually give <c>to</c> as a path, <c>{ "$path": "Customer.Email" }</c>.</summary>
public sealed record SendEmailParameters(string To, string Subject);

/// <summary>
/// Registered as "send-email". Pretends to send an email by logging it. A typed action: RuleLoom reads its parameters,
/// resolving paths, and the catalog describes them.
/// </summary>
public sealed class SendEmailAction(ILogger<SendEmailAction> logger) : IAction<SendEmailParameters>
{
    public Task<ActionResult> ExecuteAsync(SendEmailParameters parameters, ActionDefinition action, EvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Email to {To}: {Subject}", parameters.To, parameters.Subject);

        return Task.FromResult(ActionResult.Success($"Email sent to {parameters.To}."));
    }
}

public sealed class SendEmailDescriptor : ActionDescriptor<SendEmailAction, SendEmailParameters>
{
    public SendEmailDescriptor()
    {
        Label("Send email");
        Description("Emails a recipient, usually an address from the event or a context.");
        Parameter(p => p.To).Label("Recipient").Description("The email address; pick a path such as Customer.Email.");
        Parameter(p => p.Subject).Label("Subject");
    }
}
