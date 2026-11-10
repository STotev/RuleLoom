using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using RuleLoom.Abstractions.Context;
using RuleLoom.Abstractions.Values;
using RuleLoom.Schemas;

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
    private readonly Catalog? _catalog;
    private readonly IContextProviderFactory? _contextProviders;
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
    /// How date-times are read: the catalog's time zone, or <see cref="Values.TemporalRules.Utc"/> without a catalog.
    /// </summary>
    public TemporalRules TemporalRules { get; } = TemporalRules.Utc;

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
    /// Creates a new evaluation context whose <c>@name</c> contexts are the ones <paramref name="eventType"/> declares in
    /// <paramref name="catalog"/>, each loaded at most once by a provider from <paramref name="contextProviders"/>.
    /// </summary>
    /// <param name="current">The current state.</param>
    /// <param name="previous">The previous state, if any.</param>
    /// <param name="eventType">The event being processed, whose declared contexts can be loaded.</param>
    /// <param name="catalog">The catalog with the event's schema and the context providers.</param>
    /// <param name="contextProviders">Creates the providers.</param>
    public EvaluationContext(object? current, object? previous, string eventType, Catalog catalog, IContextProviderFactory contextProviders)
        : this(current, previous)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(contextProviders);

        _eventType = eventType;
        _catalog = catalog;
        _contextProviders = contextProviders;
        TemporalRules = catalog.TemporalRules;
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
    /// <exception cref="InvalidOperationException">The path isn't valid (<see cref="RulePath"/>), or names a context
    /// that the event doesn't declare (or can't be loaded) or that hasn't been loaded.</exception>
    /// <exception cref="ContextLoadException">The context's provider failed.</exception>
    public object? Resolve(string path) =>
        RulePath.TryParse(path, out var parsed, out var error) ? Resolve(parsed) : throw new InvalidOperationException(error);

    /// <summary>
    /// Resolves a parsed path: its property on the current state, the previous state or a loaded context.
    /// </summary>
    /// <returns>The resolved value, or null if the path doesn't exist.</returns>
    /// <exception cref="InvalidOperationException">The path names a context that the event doesn't declare (or can't
    /// be loaded) or that hasn't been loaded.</exception>
    /// <exception cref="ContextLoadException">The context's provider failed.</exception>
    public object? Resolve(RulePath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var root = path.Root switch
        {
            PathRoot.Context => GetLoadedContext(path.Context!),
            PathRoot.Previous => Previous,
            _ => Current
        };

        return root is null ? null : PathResolver.Shared.Resolve(root, path.Property);
    }

    /// <summary>
    /// Gets the context <paramref name="name"/>, loading it through its provider the first time; later calls (and
    /// <c>@name</c> paths) reuse the result.
    /// </summary>
    /// <param name="name">The context name, without <c>@</c> (matched ignoring case).</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The context, or <c>null</c> if the provider found none.</returns>
    /// <exception cref="InvalidOperationException">The event doesn't declare the context <paramref name="name"/>, or
    /// its declaration can't be used (see <see cref="Catalog.TryGetContext"/>).</exception>
    /// <exception cref="ContextLoadException">The provider couldn't be created or failed.</exception>
    public async ValueTask<object?> GetContextAsync(string name, CancellationToken cancellationToken = default)
    {
        var context = FindContext(name);
        var load = _contexts.GetOrAdd(name, _ => new Lazy<Task<object?>>(() => LoadAsync(context, cancellationToken)));

        return await load.Value.ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the context <paramref name="name"/> as <typeparamref name="T"/>, loading it like
    /// <see cref="GetContextAsync(string, CancellationToken)"/>. A value that already is a <typeparamref name="T"/> is
    /// returned as is; JSON (<see cref="JsonElement"/>, <see cref="JsonNode"/>) is deserialized, matching property
    /// names ignoring case.
    /// </summary>
    /// <returns>The context, or the default of <typeparamref name="T"/> if the provider found none.</returns>
    /// <exception cref="InvalidOperationException">The event doesn't declare the context <paramref name="name"/>, or
    /// its declaration can't be used.</exception>
    /// <exception cref="ContextLoadException">The provider couldn't be created or failed.</exception>
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

    private async Task<object?> LoadAsync(CatalogContext context, CancellationToken cancellationToken)
    {
        var name = context.Declaration.Name;

        try
        {
            var provider = _contextProviders!.Create(context.Provider);
            var request = new ContextRequest(name, _eventType!, Current, Previous)
            {
                Reference = ResolveReference(context.Declaration),
                TemporalRules = TemporalRules
            };

            return await provider.GetAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ContextLoadException(name, ex);
        }
    }

    // The event's declared reference to the context, resolved on the subject: one value, or the parts by name.
    private object? ResolveReference(ContextDefinition declaration) => declaration.ReferencedBy switch
    {
        null => null,
        { IsComposite: true } reference => reference.Parts.ToDictionary(part => part.Key, part => Resolve(part.Value), StringComparer.OrdinalIgnoreCase)
            as IReadOnlyDictionary<string, object?>,
        var reference => Resolve(reference.Path!)
    };

    private object? GetLoadedContext(string name)
    {
        FindContext(name);

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

    private CatalogContext FindContext(string name)
    {
        if (_catalog is null)
            throw new InvalidOperationException($"Context '@{name}' can't be loaded: this evaluation context has no catalog.");

        return _catalog.TryGetContext(_eventType!, name, out var context, out var error)
            ? context
            : throw new InvalidOperationException(error);
    }
}
