using RuleForge.Abstractions;
using RuleForge.Abstractions.Action;

namespace RuleForge.Actions;

using System.Threading;

public sealed class LogAction : IActionExecutor
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
