using System.Reflection;
using System.Text.Json;

namespace RuleForge.Abstractions.Action;

/// <summary>
/// Derives the default name an action is registered under.
/// </summary>
public static class ActionNames
{
    private const string Suffix = "Action";

    /// <summary>
    /// Gets the default name for an action type: its <see cref="ActionNameAttribute"/> if present, otherwise the class
    /// name without an <c>Action</c> suffix, in kebab-case (<c>SendEmailAction</c> → <c>send-email</c>).
    /// </summary>
    public static string GetDefaultName(Type actionType)
    {
        ArgumentNullException.ThrowIfNull(actionType);

        if (actionType.GetCustomAttribute<ActionNameAttribute>() is { } attribute)
            return attribute.Name;

        var name = actionType.Name;

        var arity = name.IndexOf('`');
        if (arity >= 0)
            name = name[..arity];

        if (name.Length > Suffix.Length && name.EndsWith(Suffix, StringComparison.Ordinal))
            name = name[..^Suffix.Length];

        return JsonNamingPolicy.KebabCaseLower.ConvertName(name);
    }
}
