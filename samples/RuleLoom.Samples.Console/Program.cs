using RuleLoom.Samples.Console.Scenarios;
using Spectre.Console;

// Each scenario is one file in Scenarios/, read top to bottom. Run one with `dotnet run -- 4`, or `dotnet run -- all`.
(int Number, string Title, Func<Task> Run)[] scenarios =
[
    (1, "First rule", FirstRule.RunAsync),
    (2, "Changes", Changes.RunAsync),
    (3, "Contexts", Contexts.RunAsync),
    (4, "Typed actions", TypedActions.RunAsync),
    (5, "Validation", Validation.RunAsync),
    (6, "Scheduling", Scheduling.RunAsync),
    (7, "Re-checked when due", RecheckedWhenDue.RunAsync),
    (8, "Overdue actions", Overdue.RunAsync),
    (9, "Dependency injection", DependencyInjection.RunAsync),
    (10, "The catalog", TheCatalog.RunAsync)
];

var choice = args.FirstOrDefault();

if (choice == "all")
{
    foreach (var scenario in scenarios)
        await scenario.Run();
}
else if (int.TryParse(choice, out var number) && scenarios.FirstOrDefault(s => s.Number == number) is { Run: not null } chosen)
{
    await chosen.Run();
}
else
{
    var table = new Table().AddColumns("#", "Scenario", "File");
    foreach (var (n, title, run) in scenarios)
        table.AddRow(n.ToString(), title, $"Scenarios/{n:00}-{run.Method.DeclaringType!.Name}.cs");

    AnsiConsole.Write(table);
    AnsiConsole.MarkupLine("Run one with [bold]dotnet run -- <number>[/], or all of them with [bold]dotnet run -- all[/].");
}
