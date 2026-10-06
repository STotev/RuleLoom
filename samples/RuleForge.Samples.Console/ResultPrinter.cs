using RuleForge.Abstractions.Rule;

namespace RuleForge.Samples.Console;

internal static class ResultPrinter
{
    public static void Print(string title, RuleEngineResult result)
    {
        System.Console.WriteLine($"=== {title} ===");

        foreach (var rule in result.Rules)
        {
            System.Console.WriteLine($"[{rule.RuleName}] {rule.Status}{(rule.Error is null ? "" : $": {rule.Error.Message}")}");

            foreach (var action in rule.ActionResults)
            {
                var when = action.ScheduledFor is { } at ? $" for {at:u}" : "";
                System.Console.WriteLine($"  #{action.ActionIndex} '{action.ActionName}' {action.Status}{when}");
            }
        }

        System.Console.WriteLine($"Has failures: {result.HasFailures}");
        System.Console.WriteLine();
    }
}
