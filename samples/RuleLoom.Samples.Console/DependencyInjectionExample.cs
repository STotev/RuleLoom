using Microsoft.Extensions.DependencyInjection;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Engine;

namespace RuleLoom.Samples.Console;

/// <summary>
/// Registration with AddRuleLoom: actions are resolved per scope and can inject your services.
/// </summary>
internal static class DependencyInjectionExample
{
    private const string Rule = """
    {
      "id": "shipped",
      "name": "Shipped",
      "eventType": "OrderUpdated",
      "condition": {
        "operator": "and",
        "operands": [
          { "operator": "changed", "operands": [{ "path": "Status" }] },
          { "operator": "eq", "operands": [{ "path": "Status" }, { "value": "Shipped" }] }
        ]
      },
      "actions": [
        { "action": "notify-customer", "parameters": { "template": "order-shipped" } }
      ]
    }
    """;

    public static async Task RunAsync()
    {
        var services = new ServiceCollection()
            .AddSingleton<IMailer, ConsoleMailer>()
            .AddRuleLoom(rules => rules.AddCatalog(catalog => catalog.AddAction<NotifyCustomerAction>()));

        await using var provider = services.BuildServiceProvider();
        await provider.StartRuleLoomAsync();   // a host does this at startup
        using var scope = provider.CreateScope();

        var engine = scope.ServiceProvider.GetRequiredService<RuleEngine>();
        var previous = new Order { Number = "SO-1042", CustomerEmail = "ada@example.com", Total = 89.90m, Status = OrderStatus.Placed };
        var current = previous with { Status = OrderStatus.Shipped };

        var result = await engine.ExecuteAsync("OrderUpdated", current, previous, [RuleJsonParser.Parse(Rule)]);

        ResultPrinter.Print("Dependency injection: AddRuleLoom", result);
    }
}

public interface IMailer
{
    void Send(string to, string template);
}

internal sealed class ConsoleMailer : IMailer
{
    public void Send(string to, string template) => System.Console.WriteLine($"  (mail '{template}' sent to {to})");
}

/// <summary>
/// Registered as "notify-customer" by convention (class name without the Action suffix, in kebab-case).
/// </summary>
internal sealed class NotifyCustomerAction(IMailer mailer) : IAction
{
    public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default)
    {
        var order = (Order)context.Current!;
        mailer.Send(order.CustomerEmail, action.GetParameter<string>("template"));

        return Task.FromResult(ActionResult.Success());
    }
}
