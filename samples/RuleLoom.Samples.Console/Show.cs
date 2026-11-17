using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Rule;
using Spectre.Console;

namespace RuleLoom.Samples.Console;

/// <summary>
/// Prints what the scenarios do. Not part of using RuleLoom: it only makes the output easy to follow.
/// </summary>
public static class Show
{
    public static void Scenario(int number, string title, string what)
    {
        AnsiConsole.Write(new Rule($"[bold]{number}. {Markup.Escape(title)}[/]").LeftJustified());
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(what)}[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>A step of the story, e.g. "Ada places an order".</summary>
    public static void Step(string text) => AnsiConsole.MarkupLine($"[bold aqua]▸[/] {Markup.Escape(text)}");

    public static void Note(string text) => AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(text)}[/]");

    /// <summary>What an action did (an email "sent", a message logged).</summary>
    public static void Effect(string text) => AnsiConsole.MarkupLine($"  [green]✉[/] {Markup.Escape(text)}");

    public static void Json(string header, string json) =>
        AnsiConsole.Write(new Panel(new Text(json.Trim())).Header(Markup.Escape(header)).BorderColor(Color.Grey));

    /// <summary>Each rule's outcome, with its actions under it.</summary>
    public static void Result(RuleEngineResult result)
    {
        var tree = new Tree("[grey]Result[/]");

        foreach (var rule in result.Rules)
        {
            var error = rule.Error is null ? "" : $": {Markup.Escape(rule.Error.Message)}";
            var node = tree.AddNode($"Rule [bold]{Markup.Escape(rule.RuleName)}[/] {Status(rule.Status)}{error}");

            foreach (var action in rule.ActionResults)
                node.AddNode(Describe(action));
        }

        AnsiConsole.Write(tree);
        AnsiConsole.WriteLine();
    }

    /// <summary>The outcome of one action, e.g. a scheduled action run when due.</summary>
    public static void Action(ActionResult action) => AnsiConsole.MarkupLine($"  {Describe(action)}");

    public static string Time(DateTimeOffset time) => time.ToUniversalTime().ToString("ddd d MMM HH:mm:ss 'UTC'");

    public static void End() => AnsiConsole.WriteLine();

    private static string Describe(ActionResult action)
    {
        var when = action.ScheduledFor is { } at ? $" for {Time(at)}" : "";
        // A scheduled action's message only repeats its time.
        var message = action.Status == ActionExecutionStatus.Scheduled ? null : action.Message ?? action.Error?.Message;
        var detail = message is null ? "" : $" [grey]({Markup.Escape(message)})[/]";
        return $"Action [bold]{Markup.Escape(action.ActionName)}[/] {Status(action.Status)}{when}{detail}";
    }

    private static string Status(RuleExecutionStatus status) => status switch
    {
        RuleExecutionStatus.Matched => "[green]matched[/]",
        RuleExecutionStatus.NotMatched => "[grey]not matched[/]",
        _ => "[red]failed[/]"
    };

    private static string Status(ActionExecutionStatus status) => status switch
    {
        ActionExecutionStatus.Executed => "[green]executed[/]",
        ActionExecutionStatus.Scheduled => "[blue]scheduled[/]",
        ActionExecutionStatus.Skipped => "[yellow]skipped[/]",
        _ => "[red]failed[/]"
    };
}
