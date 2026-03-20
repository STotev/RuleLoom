namespace BusinessRules.Abstractions;

public interface IAction
{
    Task ExecuteAsync(ActionContext context);
}