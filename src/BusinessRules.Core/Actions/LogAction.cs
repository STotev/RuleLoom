using BusinessRules.Abstractions;

namespace BusinessRules.Core.Actions;

public class LogAction : IAction
{
    public Task ExecuteAsync(ActionContext context)
    {
        Console.WriteLine($"[RULE] Executed for event: {context.Event.EventName}");
        return Task.CompletedTask;
    }
}