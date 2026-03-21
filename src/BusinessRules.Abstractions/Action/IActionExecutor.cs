namespace BusinessRules.Abstractions.Action;

public interface IActionExecutor
{
    string Type { get; }
    Task ExecuteAsync(ActionDefinition action, EvaluationContext context);
}
