namespace RuleLoom.Abstractions.Action;

/// <summary>
/// Sets the default name an action is registered under, instead of the name derived from its class
/// (see <see cref="ActionNames.GetDefaultName"/>). A name given at registration still takes precedence.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ActionNameAttribute : Attribute
{
    /// <summary>
    /// Creates the attribute.
    /// </summary>
    /// <param name="name">The default name, e.g. <c>"notify"</c>.</param>
    public ActionNameAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>
    /// The default name.
    /// </summary>
    public string Name { get; }
}
