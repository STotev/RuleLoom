using Microsoft.Extensions.DependencyInjection;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Engine;

namespace RuleLoom.Samples.Console.Scenarios;

/// <summary>
/// In an app, AddRuleLoom registers the catalog, the engine and the validator. Actions and context providers are
/// created from the container, so they can inject the app's services (here, its mailer).
/// </summary>
public static class DependencyInjection
{
    private const string ShippedRule = """
    {
      "id": "order-shipped",
      "name": "Order shipped",
      "eventType": "OrderUpdated",
      "condition": {
        "operator": "and",
        "operands": [
          { "operator": "changed", "operands": [{ "path": "Status" }] },
          { "operator": "eq", "operands": [{ "path": "Status" }, { "value": "Shipped" }] }
        ]
      },
      "actions": [{ "action": "notify-customer", "parameters": { "template": "order-shipped" } }]
    }
    """;

    public static async Task RunAsync()
    {
        Show.Scenario(9, "Dependency injection", "AddRuleLoom, and an action that uses the app's mailer.");
        Show.Json("Rule", ShippedRule);

        var services = new ServiceCollection()
            .AddSingleton<IMailer, ConsoleMailer>()
            .AddRuleLoom(ruleLoom => ruleLoom.AddCatalog(catalog => catalog
                .AddEvent<OrderUpdatedSchema>()
                .AddAction<NotifyCustomerAction>()));

        await using var provider = services.BuildServiceProvider();
        await provider.StartRuleLoomAsync();                  // a host does this at startup
        using var scope = provider.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RuleEngine>();

        Show.Step("Ada's order ships");
        var before = new Order("SO-1001", OrderStatus.Paid, 640.50m, Customers.Ada);
        var after = before with { Status = OrderStatus.Shipped };
        Show.Result(await engine.ExecuteAsync("OrderUpdated", after, before, [RuleJsonParser.Parse(ShippedRule)]));
    }
}

/// <summary>The app's mailer, registered in the container.</summary>
public interface IMailer
{
    void Send(string to, string template);
}

public sealed class ConsoleMailer : IMailer
{
    public void Send(string to, string template) => Show.Effect($"Mail \"{template}\" to {to}");
}

/// <summary>"notify-customer": named after the class, without the Action suffix. The container injects the mailer.</summary>
public sealed class NotifyCustomerAction(IMailer mailer) : IAction
{
    public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default)
    {
        var order = (Order)context.Current!;
        mailer.Send(order.Customer.Email, action.GetParameter<string>("template"));
        return Task.FromResult(ActionResult.Success());
    }
}
