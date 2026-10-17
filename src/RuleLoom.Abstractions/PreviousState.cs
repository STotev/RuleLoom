using System.Text.Json;
using System.Text.Json.Nodes;

namespace RuleLoom.Abstractions;

/// <summary>
/// A previous state described by the current state and the fields that changed, with their old values. Pass it as
/// the previous state so a producer doesn't have to send a full copy:
/// <c>engine.ExecuteAsync(eventType, current, PreviousState.FromChanges(current, changes), rules)</c>.
/// </summary>
/// <remarks>
/// Nothing is copied: a path is looked up in <see cref="Changes"/> first and falls back to <see cref="Current"/>.
/// Keys match like paths (exact case wins, otherwise a unique case-insensitive match). When the walk through
/// <see cref="Changes"/> reaches a value that isn't an object before the path ends (e.g. <c>"Customer": null</c>), the
/// previous value is <c>null</c>. A path ending at an object in <see cref="Changes"/> returns that (partial) object.
/// </remarks>
public sealed class PreviousState
{
    private PreviousState(object current, object changes)
    {
        Current = current;
        Changes = changes;
    }

    /// <summary>
    /// The current state; previous values of fields that didn't change come from here.
    /// </summary>
    public object Current { get; }

    /// <summary>
    /// The changed fields with their old values: a JSON object nested like the state.
    /// </summary>
    public object Changes { get; }

    /// <summary>
    /// Creates a previous state from the current state and the changed fields' old values.
    /// </summary>
    /// <param name="current">The current state (a .NET object or JSON).</param>
    /// <param name="changes">The changed fields with their old values, as a JSON object (<see cref="JsonElement"/> or
    /// <see cref="JsonObject"/>). An explicit <c>null</c> means the field was null; a missing key means it didn't change.</param>
    /// <exception cref="ArgumentException"><paramref name="changes"/> isn't a JSON object.</exception>
    public static PreviousState FromChanges(object current, object changes)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(changes);

        if (changes is not (JsonElement { ValueKind: JsonValueKind.Object } or JsonObject))
            throw new ArgumentException("Changes must be a JSON object (JsonElement or JsonObject).", nameof(changes));

        return new PreviousState(current, changes);
    }

    internal object? Resolve(string path, PathResolver resolver) =>
        JsonPathWalker.TryResolvePresent(Changes, path, out var value) ? value : resolver.Resolve(Current, path);
}
