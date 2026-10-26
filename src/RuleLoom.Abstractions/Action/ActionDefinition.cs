using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Scheduling;
using RuleLoom.Abstractions.Values;
using RuleLoom.Schemas;

namespace RuleLoom.Abstractions.Action;

/// <summary>
/// Represents an action to execute when a rule condition is met.
/// </summary>
public class ActionDefinition
{
    /// <summary>
    /// The name of the action to execute (e.g., "console-log", "send-email"), as registered with the action factory.
    /// In rule JSON: <c>"action": "send-email"</c>.
    /// </summary>
    public string Action { get; init; } = null!;
    
    /// <summary>
    /// Optional condition that must be met for this action to execute.
    /// Evaluated after the rule's condition passes.
    /// </summary>
    public ConditionNode? Condition { get; init; }
    
    /// <summary>
    /// Parameters for the action. The structure depends on the action.
    /// Values are plain CLR values (from JSON: <c>null</c>, <see cref="bool"/>, <see cref="int"/>/<see cref="long"/>/
    /// <see cref="double"/>, <see cref="string"/>, <see cref="List{T}"/> or <see cref="Dictionary{TKey,TValue}"/>).
    /// Read them with <see cref="GetParameter{T}"/> or <see cref="TryGetParameter{T}"/>, or all at once with
    /// <see cref="GetParameters{T}"/>.
    /// </summary>
    public Dictionary<string, object?> Parameters { get; init; } = new();

    /// <summary>
    /// Optional scheduling metadata for this action.
    /// If null, the action executes immediately.
    /// </summary>
    public ScheduleMetadata? Schedule { get; init; }

    /// <summary>
    /// Gets a parameter converted to <typeparamref name="T"/> with the shared conversion rules
    /// (<see cref="ValueConverter"/>), e.g. a JSON number to <see cref="decimal"/>, or an ISO 8601 string to
    /// <see cref="DateTimeOffset"/>. The name matches exactly, or else ignoring case when that is unambiguous.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The parameter doesn't exist.</exception>
    /// <exception cref="InvalidOperationException">The value can't be converted to <typeparamref name="T"/>.</exception>
    public T GetParameter<T>(string name)
    {
        if (!TryFindParameter(name, out var value))
            throw new KeyNotFoundException($"Action '{Action}' has no parameter '{name}'.");

        if (!ValueConverter.TryConvert<T>(value, out var converted))
        {
            throw new InvalidOperationException(
                $"Parameter '{name}' of action '{Action}' is {(value is null ? "null" : value.GetType().Name)}, " +
                $"which can't be converted to {typeof(T).Name}.");
        }

        return converted;
    }

    /// <summary>
    /// Reads all parameters as a <typeparamref name="T"/>, like System.Text.Json reads a JSON object with
    /// <see cref="ParametersSchemaDefinition.JsonOptions"/>: names ignore case, enums are read by name, constructor
    /// parameters without a default and <c>required</c> members must be present, and parameters <typeparamref name="T"/>
    /// doesn't have are ignored.
    /// </summary>
    /// <param name="context">The context the action runs in.</param>
    /// <exception cref="InvalidOperationException">The parameters can't be read as <typeparamref name="T"/>; the
    /// message names the parameter.</exception>
    public T GetParameters<T>(EvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            return JsonSerializer.SerializeToNode(Parameters, ParametersSchemaDefinition.JsonOptions)
                .Deserialize<T>(ParametersSchemaDefinition.JsonOptions)
                ?? throw new InvalidOperationException($"The parameters of action '{Action}' can't be read as {typeof(T).Name}.");
        }
        catch (JsonException ex)
        {
            var subject = ex.Path is { Length: > 2 } path && path.StartsWith("$.", StringComparison.Ordinal)
                ? $"Parameter '{path[2..]}' of action '{Action}'"
                : $"The parameters of action '{Action}'";

            throw new InvalidOperationException($"{subject} can't be read as {typeof(T).Name}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Tries to get a parameter converted to <typeparamref name="T"/>; see <see cref="GetParameter{T}"/>.
    /// </summary>
    /// <returns><c>false</c> if the parameter doesn't exist or can't be converted.</returns>
    public bool TryGetParameter<T>(string name, [MaybeNullWhen(false)] out T value)
    {
        if (TryFindParameter(name, out var raw) && ValueConverter.TryConvert(raw, out value))
            return true;

        value = default;
        return false;
    }

    private bool TryFindParameter(string name, out object? value)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (Parameters.TryGetValue(name, out value))
            return true;

        var matches = Parameters.Where(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        value = matches.Length == 1 ? matches[0].Value : null;
        return matches.Length == 1;
    }
}
