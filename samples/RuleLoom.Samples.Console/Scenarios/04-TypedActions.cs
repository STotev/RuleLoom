using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Contexts;
using RuleLoom.Engine;

namespace RuleLoom.Samples.Console.Scenarios;

/// <summary>
/// Actions are the app's code. A typed action receives its parameters as a class; the catalog describes them to rule
/// authors. A parameter can be a path, { "$path": "@customer.Email" }, which RuleLoom resolves before the action runs.
/// </summary>
public static class TypedActions
{
    private const string VipThankYou = """
    {
      "id": "vip-thank-you",
      "name": "VIP thank you",
      "eventType": "OrderPlaced",
      "condition": {
        "operator": "and",
        "operands": [
          { "operator": "gte", "operands": [{ "path": "Total" }, { "value": 500 }] },
          { "operator": "eq", "operands": [{ "path": "@customer.Tier" }, { "value": "gold" }] }
        ]
      },
      "actions": [
        {
          "action": "send-email",
          "parameters": { "to": { "$path": "@customer.Email" }, "subject": "Thank you: a gift is on its way" }
        }
      ]
    }
    """;

    public static async Task RunAsync()
    {
        Show.Scenario(4, "Typed actions", "send-email takes a recipient and a subject; the recipient is a path into the customer context.");
        Show.Json("Rule", VipThankYou);

        var catalog = new CatalogBuilder()
            .AddEvent<OrderPlacedWithCustomerSchema>()        // from scenario 3
            .AddContextProvider<CustomerProvider>("customer")
            .AddAction<SendEmailAction>()                     // named "send-email" after the class
            .Build();
        var engine = new RuleEngine(catalog, contextProviders: new DirectContextProviderFactory().Add(new CustomerProvider()));

        Show.Step("Ada (gold) places an order of 640.50");
        var order = new Order("SO-1001", OrderStatus.AwaitingPayment, 640.50m, Customers.Ada);
        Show.Result(await engine.ExecuteAsync("OrderPlaced", order, [RuleJsonParser.Parse(VipThankYou)]));
    }
}

/// <summary>The parameters of send-email, as rules give them.</summary>
public sealed record SendEmailParameters(string To, string Subject);

/// <summary>"Sends" an email by printing it. The later scenarios use it too.</summary>
public sealed class SendEmailAction : IAction<SendEmailParameters>
{
    public Task<ActionResult> ExecuteAsync(SendEmailParameters parameters, ActionDefinition action, EvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        Show.Effect($"Email to {parameters.To}: \"{parameters.Subject}\"");
        return Task.FromResult(ActionResult.Success($"sent to {parameters.To}"));
    }
}

/// <summary>Labels the action and its parameters for rule authors (scenario 10 shows where they appear).</summary>
public sealed class SendEmailDescriptor : ActionDescriptor<SendEmailAction, SendEmailParameters>
{
    public SendEmailDescriptor()
    {
        Label("Send email");
        Description("Emails a customer.");
        Parameter(p => p.To).Label("Recipient").Description("An email address, usually a path such as Customer.Email.");
        Parameter(p => p.Subject).Label("Subject");
    }
}
