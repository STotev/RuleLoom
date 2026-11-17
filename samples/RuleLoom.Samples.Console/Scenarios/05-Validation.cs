using RuleLoom.Abstractions.Rule;
using RuleLoom.Engine;
using RuleLoom.Validation;
using Spectre.Console;

namespace RuleLoom.Samples.Console.Scenarios;

/// <summary>
/// Rules are checked against the catalog before they're saved or run: fields, contexts, operators, actions and their
/// parameters. Every problem names where it is in the rule (a JSONPath), so a UI can point at it.
/// </summary>
public static class Validation
{
    private const string Rules = """
    [
      {
        "id": "vip-thank-you",
        "name": "VIP thank you",
        "eventType": "OrderPlaced",
        "condition": { "operator": "eq", "operands": [{ "path": "@customer.Tier" }, { "value": "gold" }] },
        "actions": [{ "action": "send-email", "parameters": { "to": { "$path": "@customer.Email" }, "subject": "Thank you" } }]
      },
      {
        "id": "typos",
        "name": "Typos",
        "eventType": "OrderPlaced",
        "condition": {
          "operator": "and",
          "operands": [
            { "operator": "greater", "operands": [{ "path": "Total" }, { "value": 500 }] },
            { "operator": "eq", "operands": [{ "path": "@customer.Teir" }, { "value": "gold" }] }
          ]
        },
        "actions": [{ "action": "send-email", "parameters": { "to": { "$path": "Customer.Mail" }, "subjet": "Thank you" } }]
      },
      {
        "id": "sms",
        "name": "Shipped SMS",
        "eventType": "OrderShipped",
        "condition": { "operator": "eq", "operands": [{ "path": "Status" }, { "value": "Shipped" }] },
        "actions": [{ "action": "send-sms" }]
      }
    ]
    """;

    public static Task RunAsync()
    {
        Show.Scenario(5, "Validation", "Three rules checked against the catalog: one is fine, two have mistakes.");
        Show.Json("Rules", Rules);

        var catalog = new CatalogBuilder()
            .AddEvent<OrderPlacedWithCustomerSchema>()        // from scenario 3
            .AddContextProvider<CustomerProvider>("customer")
            .AddAction<SendEmailAction>()                     // from scenario 4
            .Build();
        var validator = new RuleValidator(catalog);

        foreach (var rule in RuleJsonParser.ParseMany(Rules))
        {
            var result = validator.Validate(rule);
            if (result.IsValid)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] [bold]{Markup.Escape(rule.Name)}[/] is valid");
                continue;
            }

            AnsiConsole.MarkupLine($"[red]✗[/] [bold]{Markup.Escape(rule.Name)}[/]");
            foreach (var error in result.Errors)
                AnsiConsole.MarkupLine($"    [yellow]{Markup.Escape(error.Path)}[/]  {Markup.Escape(error.Message)}");
        }

        Show.End();
        return Task.CompletedTask;
    }
}
