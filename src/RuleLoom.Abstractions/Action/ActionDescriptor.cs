using System.Linq.Expressions;
using RuleLoom.Schemas;

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
/// registration, not the descriptor. To describe the parameters of an <see cref="IAction{TParameters}"/>, derive from
/// <see cref="ActionDescriptor{TAction, TParameters}"/>.
/// </summary>
/// <typeparam name="TAction">The described action.</typeparam>
public abstract class ActionDescriptor<TAction> : IActionDescriptor where TAction : IAction
{
    private string? _label;
    private string? _description;

    Type IActionDescriptor.ActionType => typeof(TAction);

    string? IActionDescriptor.Label => _label;

    string? IActionDescriptor.Description => _description;

    ParametersSchemaDefinition? IActionDescriptor.BuildParameters() => BuildParameters();

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

    private protected virtual ParametersSchemaDefinition? BuildParameters() => null;
}

/// <summary>
/// Describes an action with typed parameters and its parameters, configured in the constructor:
/// <code>
/// public sealed class SendEmailDescriptor : ActionDescriptor&lt;SendEmailAction, SendEmailParameters&gt;
/// {
///     public SendEmailDescriptor()
///     {
///         Label("Send email");
///         Parameter(p =&gt; p.To).Label("Recipient");
///         Parameter(p =&gt; p.TrackingId).Ignore();
///     }
/// }
/// </code>
/// The parameters are described by <typeparamref name="TParameters"/> (see <see cref="IAction{TParameters}"/>); only
/// configured labels and descriptions are added.
/// </summary>
/// <typeparam name="TAction">The described action.</typeparam>
/// <typeparam name="TParameters">The action's parameters type.</typeparam>
public abstract class ActionDescriptor<TAction, TParameters> : ActionDescriptor<TAction> where TAction : IAction<TParameters>
{
    private readonly ParametersSchema<TParameters> _parameters = new();

    /// <summary>
    /// Configures a parameter, or a field of one at any depth (<c>p =&gt; p.Recipient.Email</c>): its label and
    /// description, or <c>Ignore()</c> to leave it out of the catalog.
    /// </summary>
    protected FieldBuilder Parameter<TProperty>(Expression<Func<TParameters, TProperty>> parameter) => _parameters.Parameter(parameter);

    private protected override ParametersSchemaDefinition BuildParameters() => _parameters.Build();
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

    /// <summary>
    /// Generates the schema of the action's parameters; <c>null</c> when the descriptor doesn't describe them.
    /// </summary>
    ParametersSchemaDefinition? BuildParameters();
}
