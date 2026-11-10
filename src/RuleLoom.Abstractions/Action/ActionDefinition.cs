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
    /// <see cref="double"/>, <see cref="string"/>, <see cref="List{T}"/> or <see cref="Dictionary{TKey,TValue}"/>), or
    /// a <see cref="RulePath"/> for a path (in JSON: <c>{ "$path": "@customer.Email" }</c>), resolved when read.
    /// Read them with <see cref="GetParametersAsync{T}"/> (all at once), <see cref="GetParameterAsync{T}"/>, or, for
    /// values only, <see cref="GetParameter{T}"/> and <see cref="TryGetParameter{T}"/>.
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
    /// A path can't be read without the event: use <see cref="GetParameterAsync{T}"/>. Without the event there's no
    /// catalog either, so a date-time without an offset is read as UTC; <see cref="GetParameterAsync{T}"/> reads it in the
    /// catalog's time zone.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The parameter doesn't exist.</exception>
    /// <exception cref="InvalidOperationException">The value is a path or can't be converted to <typeparamref name="T"/>.</exception>
    /// <exception cref="InvalidTemporalValueException">The value is a wall-clock time that doesn't exist in its zone.</exception>
    public T GetParameter<T>(string name)
    {
        var value = FindParameter(name);

        if (value is RulePath path)
        {
            throw new InvalidOperationException(
                $"Parameter '{name}' of action '{Action}' is the path '{path}'; read it with {nameof(GetParameterAsync)}.");
        }

        return Convert<T>(name, value, null, TemporalRules.Utc);
    }

    /// <summary>
    /// Gets a parameter like <see cref="GetParameter{T}"/>, resolving it first when it's a path: on the event's state or,
    /// for <c>@name</c> paths, on the context, which is loaded if it hasn't been. Date-times are read with the context's
    /// <see cref="EvaluationContext.TemporalRules"/>.
    /// </summary>
    /// <param name="name">The parameter's name.</param>
    /// <param name="context">The context the action runs in.</param>
    /// <param name="cancellationToken">Cancels loading a context.</param>
    /// <exception cref="KeyNotFoundException">The parameter doesn't exist.</exception>
    /// <exception cref="InvalidOperationException">The path can't be resolved, or the value can't be converted to
    /// <typeparamref name="T"/>.</exception>
    /// <exception cref="InvalidTemporalValueException">The value is a wall-clock time that doesn't exist in its zone.</exception>
    public async ValueTask<T> GetParameterAsync<T>(string name, EvaluationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var value = FindParameter(name);
        var path = value as RulePath;
        if (path is not null)
            value = await ResolveAsync(name, path, context, cancellationToken).ConfigureAwait(false);

        return Convert<T>(name, value, path, context.TemporalRules);
    }

    /// <summary>
    /// Reads all parameters as a <typeparamref name="T"/>, after resolving the paths among them (see
    /// <see cref="GetParameterAsync{T}"/>), like System.Text.Json reads a JSON object with
    /// <see cref="ParametersSchemaDefinition.JsonOptions"/>: names ignore case, enums are read by name, constructor
    /// parameters without a default and <c>required</c> members must be present, and parameters <typeparamref name="T"/>
    /// doesn't have are ignored. Date-times are read with the context's <see cref="EvaluationContext.TemporalRules"/>.
    /// </summary>
    /// <param name="context">The context the action runs in.</param>
    /// <param name="cancellationToken">Cancels loading a context.</param>
    /// <exception cref="InvalidOperationException">A path can't be resolved, or the parameters can't be read as
    /// <typeparamref name="T"/>; the message names the parameter.</exception>
    /// <exception cref="InvalidTemporalValueException">A date-time is a wall-clock time that doesn't exist in its zone.</exception>
    public async ValueTask<T> GetParametersAsync<T>(EvaluationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var values = new Dictionary<string, object?>(Parameters.Count);
        foreach (var (name, value) in Parameters)
        {
            values[name] = value is RulePath path
                ? await ResolveAsync(name, path, context, cancellationToken).ConfigureAwait(false)
                : value;
        }

        var options = TemporalJsonConverters.Options(context.TemporalRules);

        try
        {
            return JsonSerializer.SerializeToNode(values, options).Deserialize<T>(options)
                ?? throw new InvalidOperationException($"The parameters of action '{Action}' can't be read as {typeof(T).Name}.");
        }
        catch (InvalidTemporalValueException ex)
        {
            // Name the parameter (or the path it was read from) the invalid date-time came from.
            var (name, value) = values.FirstOrDefault(v => v.Value is not null && TemporalRules.Format(v.Value) == ex.Value);
            throw name is null ? ex : Attribute(ex, name, Parameters[name] as RulePath);
        }
        catch (JsonException ex)
        {
            var subject = ex.Path is { Length: > 2 } jsonPath && jsonPath.StartsWith("$.", StringComparison.Ordinal)
                ? $"Parameter '{jsonPath[2..]}' of action '{Action}'"
                : $"The parameters of action '{Action}'";

            throw new InvalidOperationException($"{subject} can't be read as {typeof(T).Name}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Tries to get a parameter converted to <typeparamref name="T"/>; see <see cref="GetParameter{T}"/>.
    /// </summary>
    /// <returns><c>false</c> if the parameter doesn't exist, is a path or can't be converted.</returns>
    public bool TryGetParameter<T>(string name, [MaybeNullWhen(false)] out T value)
    {
        if (TryFindParameter(name, out var raw) && raw is not RulePath && ValueConverter.TryConvert(raw, TemporalRules.Utc, out value))
            return true;

        value = default;
        return false;
    }

    private object? FindParameter(string name) =>
        TryFindParameter(name, out var value) ? value : throw new KeyNotFoundException($"Action '{Action}' has no parameter '{name}'.");

    private T Convert<T>(string name, object? value, RulePath? path, TemporalRules temporalRules)
    {
        bool converted;
        T? result;

        try
        {
            converted = ValueConverter.TryConvert(value, temporalRules, out result);
        }
        catch (InvalidTemporalValueException ex)
        {
            throw Attribute(ex, name, path);
        }

        return converted
            ? result!
            : throw new InvalidOperationException(
                $"Parameter '{name}' of action '{Action}' is {(value is null ? "null" : value.GetType().Name)}, " +
                $"which can't be converted to {typeof(T).Name}.");
    }

    // An invalid date-time names the path it was read from, or else the parameter.
    private InvalidTemporalValueException Attribute(InvalidTemporalValueException ex, string name, RulePath? path) =>
        path is not null ? ex.At(path.ToString()) : ex.From($"Parameter '{name}' of action '{Action}'");

    // Loads the context a path reads, if it isn't loaded yet (e.g. a scheduled action run outside the engine).
    private async ValueTask<object?> ResolveAsync(string name, RulePath path, EvaluationContext context, CancellationToken cancellationToken)
    {
        try
        {
            if (path.Root == PathRoot.Context)
                await context.GetContextAsync(path.Context!, cancellationToken).ConfigureAwait(false);

            return context.Resolve(path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Parameter '{name}' of action '{Action}' can't be resolved ('{path}'): {ex.Message}", ex);
        }
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
