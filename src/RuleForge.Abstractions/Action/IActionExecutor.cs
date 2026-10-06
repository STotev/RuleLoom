namespace RuleForge.Abstractions.Action;

/// <summary>
/// Defines an executor for rule actions. Rules refer to it by the name it is registered under: by default its class
/// name without an <c>Action</c> suffix in kebab-case (<c>SendEmailAction</c> → <c>send-email</c>), or the name in its
/// <see cref="ActionNameAttribute"/>, or a name given at registration (see <see cref="ActionNames"/>).
/// </summary>
public interface IActionExecutor
{
    /// <summary>
    /// Executes the action with the given parameters and context.
    /// </summary>
    /// <param name="action">The action definition with its name and parameters.</param>
    /// <param name="context">The evaluation context containing current/previous state.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context,
        CancellationToken cancellationToken = default);
}
