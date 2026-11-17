using RuleLoom.Schemas;
using Spectre.Console;

namespace RuleLoom.Samples.Console.Scenarios;

/// <summary>
/// The catalog describes itself as a document (JSON Schema underneath): the events with their fields and contexts, the
/// contexts' fields, the operators and the actions with their parameters. A rule-authoring UI builds its pickers from
/// it; RuleLoom.AspNetCore serves it as GET {prefix}/catalog.
/// </summary>
public static class TheCatalog
{
    public static Task RunAsync()
    {
        Show.Scenario(10, "The catalog", "What a rule-authoring UI can offer, read from the catalog of scenarios 3 and 4.");

        var catalog = new CatalogBuilder()
            .AddEvent<OrderPlacedWithCustomerSchema>()        // from scenario 3
            .AddContextProvider<CustomerProvider>("customer")
            .AddAction<SendEmailAction>()                     // from scenario 4, with its descriptor
            .Build();
        var document = catalog.ToDocument();

        var tree = new Tree("[bold]Catalog[/]");

        var events = tree.AddNode("Events");
        foreach (var @event in document.Events)
        {
            var node = events.AddNode($"[bold]{@event.EventType}[/] [purple]{Markup.Escape(@event.Label ?? "")}[/]");
            AddFields(node, @event.Fields, prefix: "");
            foreach (var context in @event.Contexts)
                node.AddNode($"[aqua]@{context.Name}[/] [purple]{Markup.Escape(context.Label ?? "")}, loaded by \"{context.Provider}\"[/]");
        }

        var contexts = tree.AddNode("Contexts");
        foreach (var context in document.Contexts)
            AddFields(contexts.AddNode($"[aqua]{context.Name}[/]"), context.Fields, prefix: "");

        var actions = tree.AddNode("Actions");
        foreach (var action in document.Actions)
        {
            var node = actions.AddNode($"[bold]{action.Name}[/] [purple]{Markup.Escape(action.Label ?? "")}: {Markup.Escape(action.Description ?? "")}[/]");
            if (action.Parameters is { } parameters)
                AddFields(node, parameters.Fields, prefix: "");
        }

        tree.AddNode($"Operators [purple]{string.Join(", ", document.Operators.Select(o => o.Name))}[/]");

        AnsiConsole.Write(tree);
        Show.Note($"As JSON, this is {catalog.ToJson().Length:N0} characters: catalog.ToJson().");
        Show.End();
        return Task.CompletedTask;
    }

    private static void AddFields(TreeNode node, IReadOnlyList<FieldDefinition> fields, string prefix)
    {
        foreach (var field in fields)
        {
            var label = field.Label is null ? "" : $" [purple]{Markup.Escape(field.Label)}[/]";
            var values = field.EnumValues.Count == 0 ? "" : $" [purple]({string.Join(", ", field.EnumValues)})[/]";
            node.AddNode($"{prefix}{field.Name}: [blue]{field.Type}[/]{values}{label}");
            AddFields(node, field.Fields, $"{prefix}{field.Name}.");
        }
    }
}
