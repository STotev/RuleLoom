using RuleForge.Abstractions;
using RuleForge.Abstractions.Action;

namespace RuleForge.Actions;

/// <summary>
/// Writes the <c>message</c> parameter to the console; meant for development and testing. Registered as
/// <c>console-log</c>, and only when added explicitly (<c>AddRuleForge</c> doesn't register it).
/// </summary>
public sealed class ConsoleLogAction : IAction
{
    public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        if (action.Parameters.TryGetValue("message", out var message))
        {
            Console.WriteLine(message);
        }

        return Task.FromResult(ActionResult.Success());
    }
}
