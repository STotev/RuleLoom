using RuleForge.Abstractions;
using RuleForge.Abstractions.Action;

namespace RuleForge.Core.Actions;

public class LogAction : IActionExecutor
{
    public string Type => "log";

    public Task ExecuteAsync(ActionDefinition action, EvaluationContext context)
    {
        if (action.Parameters.TryGetValue("message", out var message))
        {
            Console.WriteLine(message);
        }

        return Task.CompletedTask;
    }
}
