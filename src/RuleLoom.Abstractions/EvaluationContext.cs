using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using RuleLoom.Abstractions.Context;

namespace RuleLoom.Abstractions;

/// <summary>
/// Provides the evaluation context for rule execution, containing current and previous state, and the contexts
/// (related data) used by the event's rules.
/// </summary>
public class EvaluationContext
{
    // Context JSON is read like paths: property names ignore case.
    private static readonly JsonSerializerOptions ContextJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string? _eventType;
    private readonly IContextProviderFactory? _contextProviders;
    private readonly IContextReferenceSource? _contextReferences;
    private readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _contexts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The current state of the object being evaluated.
    /// </summary>
    public object? Current { get; }

    /// <summary>
    /// The previous state of the object (optional). Used for change detection.
    /// </summary>
    public object? Previous { get; }

    /// <summary>
    /// Creates a new evaluation context without contexts.
    /// </summary>
    /// <param name="current">The current state.</param>
    /// <param name="previous">The previous state (optional).</param>
    public EvaluationContext(object? current, object? previous = null)
    {
        Current = current;
        Previous = previous;
    }

    /// <summary>
    /// Creates a new evaluation context whose <c>@name</c> contexts are loaded through
    /// <paramref name="contextProviders"/>, each at most once.
    /// </summary>
    /// <param name="current">The current state.</param>
    /// <param name="previous">The previous state, if any.</param>
    /// <param name="eventType">The event being processed, passed to the providers.</param>
    /// <param name="contextProviders">The registered context providers.</param>
    /// <param name="contextReferences">How the event refers to its contexts (e.g. from its schema), resolved and passed to
    /// providers as <see cref="ContextRequest.Reference"/>; without it, providers read the subject themselves.</param>
    public EvaluationContext(object? current, object? previous, string eventType, IContextProviderFactory contextProviders,
        IContextReferenceSource? contextReferences = null)
        : this(current, previous)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(contextProviders);

        _eventType = eventType;
        _contextProviders = contextProviders;
        _contextReferences = contextReferences;
    }

    /// <summary>
    /// Resolves a property path from the context.
    /// A leading <c>$current.</c> or <c>$previous.</c> (case-insensitive) selects the state; any other path,
    /// including nested ones such as <c>Address.City</c>, resolves against <see cref="Current"/>.
    /// <c>$current</c> or <c>$previous</c> alone returns the whole state object. Other names starting with <c>$</c>
    /// are reserved. A leading <c>@name</c> resolves on the context <c>name</c>, which must already be loaded (the
    /// engine loads the contexts its rules use; see <see cref="GetContextAsync"/>).
    /// </summary>
    /// <param name="path">The property path to resolve.</param>
    /// <returns>The resolved value, or null if the path doesn't exist.</returns>
    /// <exception cref="InvalidOperationException">The path starts with an unknown <c>$</c> variable, or names a
    /// context that has no provider or hasn't been loaded.</exception>
    /// <exception cref="ContextLoadException">The context's provider failed.</exception>
    public object? Resolve(string path)
    {
        var parts = path.Split('.', 2);

        object? root = Current;
        var remainingPath = path;

        if (parts[0].StartsWith('@'))
        {
            root = GetLoadedContext(parts[0][1..]);
            remainingPath = parts.Length == 2 ? parts[1] : string.Empty;
        }
        else if (parts[0].StartsWith('$'))
        {
            if (parts[0].Equals("$previous", StringComparison.OrdinalIgnoreCase))
            {
                root = Previous;
            }
            else if (!parts[0].Equals("$current", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Unknown variable '{parts[0]}' in path '{path}'. Use $current or $previous.");
            }

            remainingPath = parts.Length == 2 ? parts[1] : string.Empty;
        }

        if (root == null)
            return null;

        return PathResolver.Shared.Resolve(root, remainingPath);
    }

    /// <summary>
    /// Gets the context <paramref name="name"/>, loading it through its provider the first time; later calls (and
    /// <c>@name</c> paths) reuse the result.
    /// </summary>
    /// <param name="name">The context name, without <c>@</c> (matched ignoring case).</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The context, or <c>null</c> if the provider found none.</returns>
    /// <exception cref="InvalidOperationException">No provider is registered under <paramref name="name"/>.</exception>
    /// <exception cref="ContextLoadException">The provider failed.</exception>
    public async ValueTask<object?> GetContextAsync(string name, CancellationToken cancellationToken = default)
    {
        var provider = GetProvider(name);
        var load = _contexts.GetOrAdd(name, _ => new Lazy<Task<object?>>(() => LoadAsync(provider, name, cancellationToken)));

        return await load.Value.ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the context <paramref name="name"/> as <typeparamref name="T"/>, loading it like
    /// <see cref="GetContextAsync(string, CancellationToken)"/>. A value that already is a <typeparamref name="T"/> is
    /// returned as is; JSON (<see cref="JsonElement"/>, <see cref="JsonNode"/>) is deserialized, matching property
    /// names ignoring case.
    /// </summary>
    /// <returns>The context, or the default of <typeparamref name="T"/> if the provider found none.</returns>
    /// <exception cref="InvalidOperationException">No provider is registered under <paramref name="name"/>.</exception>
    /// <exception cref="ContextLoadException">The provider failed.</exception>
    /// <exception cref="InvalidCastException">The context is neither a <typeparamref name="T"/> nor JSON.</exception>
    /// <exception cref="JsonException">The context's JSON can't be read as <typeparamref name="T"/>.</exception>
    public async ValueTask<T?> GetContextAsync<T>(string name, CancellationToken cancellationToken = default)
    {
        var value = await GetContextAsync(name, cancellationToken).ConfigureAwait(false);

        return value switch
        {
            null => default,
            T typed => typed,
            JsonElement element => element.Deserialize<T>(ContextJsonOptions),
            JsonNode node => node.Deserialize<T>(ContextJsonOptions),
            _ => throw new InvalidCastException($"Context '@{name}' is a {value.GetType().Name}, not a {typeof(T).Name}.")
        };
    }

    private async Task<object?> LoadAsync(IContextProvider provider, string name, CancellationToken cancellationToken)
    {
        try
        {
            var request = new ContextRequest(name, _eventType!, Current, Previous)
            {
                Reference = await ResolveReferenceAsync(name, cancellationToken).ConfigureAwait(false)
            };

            return await provider.GetAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ContextLoadException(name, ex);
        }
    }

    /// <summary>
    /// The event's declared reference to the context, resolved on the current state: one value, or the parts by name.
    /// </summary>
    private async ValueTask<object?> ResolveReferenceAsync(string name, CancellationToken cancellationToken)
    {
        if (_contextReferences is null ||
            await _contextReferences.GetReferenceAsync(_eventType!, name, cancellationToken).ConfigureAwait(false) is not { } reference)
        {
            return null;
        }

        return reference.IsComposite
            ? reference.Parts.ToDictionary(part => part.Key, part => Resolve(part.Value), StringComparer.OrdinalIgnoreCase)
                as IReadOnlyDictionary<string, object?>
            : Resolve(reference.Path!);
    }

    private object? GetLoadedContext(string name)
    {
        GetProvider(name);

        if (!_contexts.TryGetValue(name, out var load) || !load.Value.IsCompleted)
        {
            throw new InvalidOperationException(
                $"Context '@{name}' hasn't been loaded. The engine loads contexts used in rule paths; " +
                $"load others with {nameof(GetContextAsync)}.");
        }

        if (load.Value.Exception is { } failure)
            ExceptionDispatchInfo.Throw(failure.InnerException ?? failure);

        if (load.Value.IsCanceled)
            throw new OperationCanceledException($"Loading context '@{name}' was cancelled.");

        return load.Value.Result;
    }

    private IContextProvider GetProvider(string name)
    {
        IContextProvider? provider = null;

        if (_contextProviders is null || !_contextProviders.TryGetProvider(name, out provider) || provider is null)
            throw new InvalidOperationException($"No context provider is registered for '@{name}'.");

        return provider;
    }
}
