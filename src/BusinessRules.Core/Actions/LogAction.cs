using BusinessRules.Abstractions;
using BusinessRules.Abstractions.Action;

namespace BusinessRules.Core.Actions;

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
