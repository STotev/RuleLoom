namespace RuleLoom.Abstractions.Action;

/// <summary>
/// An action that rules can run. Rules refer to it by the name it is registered under: by default its class
/// name without an <c>Action</c> suffix in kebab-case (<c>SendEmailAction</c> → <c>send-email</c>), or the name in its
/// <see cref="ActionNameAttribute"/>, or a name given at registration (see <see cref="ActionNames"/>).
/// Implement <see cref="IAction{TParameters}"/> instead to receive the parameters as a typed object.
/// </summary>
public interface IAction
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

/// <summary>
/// An action that receives its parameters as a <typeparamref name="TParameters"/>, read from the rule with
/// <see cref="ActionDefinition.GetParameters{T}"/>. The catalog describes its parameters from the type (and an
/// <see cref="ActionDescriptor{TAction, TParameters}"/>, if any): constructor parameters without a default and
/// <c>required</c> members are required.
/// </summary>
/// <typeparam name="TParameters">The parameters type: a class or record whose properties are the parameters.</typeparam>
public interface IAction<TParameters> : IAction
{
    /// <summary>
    /// Executes the action with its parameters.
    /// </summary>
    /// <param name="parameters">The rule's parameters, read as <typeparamref name="TParameters"/>.</param>
    /// <param name="action">The action definition with its name and raw parameters.</param>
    /// <param name="context">The evaluation context containing current/previous state.</param>
    /// <param name="cancellationToken">CancellationToken</param>
    Task<ActionResult> ExecuteAsync(TParameters parameters, ActionDefinition action, EvaluationContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the parameters and executes the action with them.
    /// </summary>
    /// <exception cref="InvalidOperationException">The parameters can't be read as <typeparamref name="TParameters"/>.</exception>
    Task<ActionResult> IAction.ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken) =>
        ExecuteAsync(action.GetParameters<TParameters>(context), action, context, cancellationToken);
}
