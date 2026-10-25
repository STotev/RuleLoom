namespace RuleLoom.Abstractions.Action;

/// <summary>
/// Describes an action for authoring UIs, configured in the constructor and shipped next to the action (e.g. in the
/// same package):
/// <code>
/// public sealed class SendEmailDescriptor : ActionDescriptor&lt;SendEmailAction&gt;
/// {
///     public SendEmailDescriptor()
///     {
///         Label("Send email");
///         Description("Sends a templated email.");
///     }
/// }
/// </code>
/// A catalog finds the descriptor in the action's assembly when the action is added. The name rules use belongs to the
/// registration, not the descriptor.
/// </summary>
/// <typeparam name="TAction">The described action.</typeparam>
public abstract class ActionDescriptor<TAction> : IActionDescriptor where TAction : IAction
{
    private string? _label;
    private string? _description;

    Type IActionDescriptor.ActionType => typeof(TAction);

    string? IActionDescriptor.Label => _label;

    string? IActionDescriptor.Description => _description;

    /// <summary>A readable name for authoring UIs.</summary>
    protected void Label(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        _label = label;
    }

    /// <summary>A longer explanation of what the action does.</summary>
    protected void Description(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        _description = description;
    }
}

/// <summary>
/// An action's description, read by a catalog; implemented by <see cref="ActionDescriptor{TAction}"/>.
/// </summary>
public interface IActionDescriptor
{
    /// <summary>The described action's type.</summary>
    Type ActionType { get; }

    /// <summary>A readable name for authoring UIs, if configured.</summary>
    string? Label { get; }

    /// <summary>An optional longer explanation.</summary>
    string? Description { get; }
}
