using Microsoft.Extensions.DependencyInjection;
using RuleForge.Abstractions;
using RuleForge.Abstractions.Action;
using RuleForge.Abstractions.Rule;
using RuleForge.Engine;

namespace RuleForge.Samples.Console;

/// <summary>
/// Registration with AddRuleForge: actions are resolved per scope and can inject your services.
/// </summary>
internal static class DependencyInjectionExample
{
    private const string Rule = """
    {
      "name": "Sold",
      "eventType": "PropertyUpdated",
      "condition": {
        "operator": "and",
        "operands": [
          { "operator": "changed", "operands": [{ "path": "Status" }] },
          { "operator": "eq", "operands": [{ "path": "Status" }, { "value": "Sold" }] }
        ]
      },
      "actions": [
        { "action": "notify-agent", "parameters": { "template": "congratulations" } }
      ]
    }
    """;

    public static async Task RunAsync()
    {
        var services = new ServiceCollection()
            .AddSingleton<IMailer, ConsoleMailer>()
            .AddRuleForge(rules => rules.AddAction<NotifyAgentAction>());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var engine = scope.ServiceProvider.GetRequiredService<RuleEngine>();
        var previous = new Property { Address = "3 Pine Lane", Price = 510_000m, Status = ListingStatus.Active };
        var current = previous with { Status = ListingStatus.Sold };

        var result = await engine.ExecuteAsync("PropertyUpdated", current, previous, [RuleJsonParser.Parse(Rule)]);

        ResultPrinter.Print("Dependency injection: AddRuleForge", result);
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
/// Registered as "notify-agent" by convention (class name without the Action suffix, in kebab-case).
/// </summary>
internal sealed class NotifyAgentAction(IMailer mailer) : IAction
{
    public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default)
    {
        var property = (Property)context.Current!;
        mailer.Send($"agent+{property.Address.Replace(' ', '-').ToLowerInvariant()}@example.com", action.GetParameter<string>("template"));

        return Task.FromResult(ActionResult.Success());
    }
}
