using System.Diagnostics.CodeAnalysis;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Context;
using RuleLoom.Schemas;

namespace RuleLoom.Abstractions;

/// <summary>
/// The vocabulary an application's rules are written in: its event types (with the contexts they declare), context
/// providers, operators and actions. Built once (usually by <c>CatalogBuilder</c>) and fixed afterwards; the engine and
/// the validator run on it, and <see cref="ToJson"/> describes it for authoring UIs.
/// </summary>
/// <remarks>
/// A catalog is built from code, optionally merged with what a catalog store holds. Code always wins over stored
/// entries of the same name. Broken links in code (a context without a provider, a reference to a missing field,
/// duplicate or unusable names) fail the constructor; the same links broken in stored entries become
/// <see cref="Problems"/>, which fail only the rules that use them.
/// </remarks>
public sealed class Catalog
{
    private readonly Dictionary<string, EventSchemaDefinition> _events = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ContextProviderRegistration> _providers;
    private readonly Dictionary<string, IConditionOperator> _operators;
    private readonly Dictionary<string, ActionRegistration> _actions;

    // Contexts declared by stored events whose links are broken, by "event/context": the problem's message.
    private readonly Dictionary<string, string> _brokenContexts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a catalog from code-defined entries and, optionally, what a catalog store holds.
    /// </summary>
    /// <param name="events">The event schemas defined in code.</param>
    /// <param name="contextProviders">The registered context providers.</param>
    /// <param name="operators">The operators rule conditions can use.</param>
    /// <param name="actions">The registered actions.</param>
    /// <param name="stored">What a catalog store holds: its event and context schemas are added where code defines
    /// none; its operators and actions are ignored (they can't run without code).</param>
    /// <exception cref="InvalidOperationException">An entry defined in code is broken; the message lists every
    /// problem.</exception>
    public Catalog(
        IEnumerable<EventSchemaDefinition> events,
        IEnumerable<ContextProviderRegistration> contextProviders,
        IEnumerable<IConditionOperator> operators,
        IEnumerable<ActionRegistration> actions,
        CatalogDocument? stored = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(contextProviders);
        ArgumentNullException.ThrowIfNull(operators);
        ArgumentNullException.ThrowIfNull(actions);

        var errors = new List<string>();
        var problems = new List<CatalogProblem>();

        _operators = Index(operators, o => o.Name, "Operator", errors, usedInRules: true);
        _actions = Index(actions, a => a.Name, "Action", errors, usedInRules: true);
        _providers = Index(contextProviders, p => p.Name, "Context provider", errors, usedInRules: false);

        foreach (var (eventType, schema) in Index(events, e => e.EventType, "Event", errors, usedInRules: false))
            _events.Add(eventType, schema);

        var storedEvents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (stored is not null)
        {
            foreach (var schema in stored.Events.Where(e => _events.TryAdd(e.EventType, e)))
                storedEvents.Add(schema.EventType);

            foreach (var schema in stored.Contexts)
            {
                if (!_providers.TryGetValue(schema.Name, out var provider))
                    problems.Add(new CatalogProblem(CatalogEntryKind.Context, schema.Name, $"No context provider is registered under '{schema.Name}'."));
                else if (provider.Schema is null)
                    _providers[provider.Name] = provider with { Schema = schema };
            }
        }

        foreach (var schema in _events.Values)
        {
            foreach (var context in schema.Contexts)
            {
                if (FindBrokenLink(schema, context) is not { } message)
                    continue;

                if (storedEvents.Contains(schema.EventType))
                {
                    problems.Add(new CatalogProblem(CatalogEntryKind.Event, schema.EventType, message));
                    _brokenContexts[$"{schema.EventType}/{context.Name}"] = message;
                }
                else
                {
                    errors.Add($"Event '{schema.EventType}': {message}");
                }
            }
        }

        if (errors.Count > 0)
            throw new InvalidOperationException($"The catalog is invalid:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", errors)}");

        Problems = problems;
    }

    /// <summary>The event schemas: code-defined first, then those only the store holds.</summary>
    public IReadOnlyList<EventSchemaDefinition> Events => [.. _events.Values];

    /// <summary>The registered context providers, with the schema of what they load (from code, else the store).</summary>
    public IReadOnlyList<ContextProviderRegistration> ContextProviders => [.. _providers.Values];

    /// <summary>The operators rule conditions can use.</summary>
    public IReadOnlyList<IConditionOperator> Operators => [.. _operators.Values];

    /// <summary>The registered actions.</summary>
    public IReadOnlyList<ActionRegistration> Actions => [.. _actions.Values];

    /// <summary>The broken links found in store-contributed entries; each fails only the rules that use it.</summary>
    public IReadOnlyList<CatalogProblem> Problems { get; }

    /// <summary>Finds the schema of <paramref name="eventType"/> (ignoring case).</summary>
    public bool TryGetEvent(string eventType, [NotNullWhen(true)] out EventSchemaDefinition? schema) =>
        _events.TryGetValue(eventType, out schema);

    /// <summary>Finds the context provider registered under <paramref name="name"/> (ignoring case).</summary>
    public bool TryGetContextProvider(string name, [NotNullWhen(true)] out ContextProviderRegistration? provider) =>
        _providers.TryGetValue(name, out provider);

    /// <summary>Finds the operator named <paramref name="name"/> (ignoring case).</summary>
    public bool TryGetOperator(string name, [NotNullWhen(true)] out IConditionOperator? @operator) =>
        _operators.TryGetValue(name, out @operator);

    /// <summary>Finds the action registered under <paramref name="name"/> (ignoring case).</summary>
    public bool TryGetAction(string name, [NotNullWhen(true)] out ActionRegistration? action) =>
        _actions.TryGetValue(name, out action);

    /// <summary>
    /// Finds the context <paramref name="name"/> of <paramref name="eventType"/>: the event's declaration and the
    /// provider that loads it. Contexts are only found through their event's declaration.
    /// </summary>
    /// <param name="eventType">The event type (ignoring case).</param>
    /// <param name="name">The context name, without <c>@</c> (ignoring case).</param>
    /// <param name="context">The declaration and its provider, when found.</param>
    /// <param name="error">Why it can't be used: the event is unknown, doesn't declare it, or its link is broken.</param>
    public bool TryGetContext(string eventType, string name,
        [NotNullWhen(true)] out CatalogContext? context, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(name);

        context = null;

        if (!_events.TryGetValue(eventType, out var schema))
        {
            error = $"'@{name}' can't be loaded: event '{eventType}' isn't in the catalog, so it declares no contexts.";
            return false;
        }

        if (schema.Contexts.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) is not { } declaration)
        {
            error = $"'@{name}' is not a context of {schema.EventType}.";
            return false;
        }

        if (_brokenContexts.TryGetValue($"{schema.EventType}/{declaration.Name}", out var problem))
        {
            error = problem;
            return false;
        }

        // Only stored events can get here without a provider, and they're in _brokenContexts.
        context = new CatalogContext(declaration, _providers[declaration.Provider]);
        error = null;
        return true;
    }

    /// <summary>
    /// Describes the catalog as a document: what a catalog store keeps and authoring UIs read.
    /// </summary>
    public CatalogDocument ToDocument() => new(
        _events.Values,
        _providers.Values.Select(p => p.Schema).OfType<ContextSchemaDefinition>(),
        _operators.Values.Select(o => new CatalogOperator(o.Name, o.Label, o.Description, o.MinOperands,
            o.MaxOperands == int.MaxValue ? null : o.MaxOperands)),
        _actions.Values.Select(a => new CatalogAction(a.Name, a.Label, a.Description)),
        Problems);

    /// <summary>
    /// Writes the catalog as JSON (<see cref="CatalogDocument.ToJson"/>), e.g. to generate an authoring UI.
    /// </summary>
    public string ToJson() => ToDocument().ToJson();

    private string? FindBrokenLink(EventSchemaDefinition schema, ContextDefinition context)
    {
        if (!_providers.ContainsKey(context.Provider))
            return $"No context provider is registered under '{context.Provider}' (context '@{context.Name}').";

        var paths = context.ReferencedBy is not { } reference ? []
            : reference.IsComposite ? reference.Parts.Values : [reference.Path!];

        return paths.FirstOrDefault(path => !IsField(schema, path)) is { } missing
            ? $"Context '@{context.Name}' is referenced by '{missing}', which isn't a field of {schema.EventType}."
            : null;
    }

    // Whether a reference path names a field of the event (fields without declared nested fields can't be checked).
    private static bool IsField(EventSchemaDefinition schema, string path)
    {
        if (!RulePath.TryParse(path, out var parsed, out _) || parsed.Root == PathRoot.Context || parsed.Segments.Count == 0)
            return false;

        var fields = schema.Fields;
        foreach (var segment in parsed.Segments)
        {
            if (FieldDefinition.Find(fields, segment) is not { } field)
                return false;

            if (field.Type is FieldType.Object or FieldType.Any && field.Fields.Count == 0)
                return true;

            fields = field.Fields;
        }

        return true;
    }

    private static Dictionary<string, T> Index<T>(IEnumerable<T> items, Func<T, string> name, string kind, List<string> errors, bool usedInRules)
    {
        var index = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item, nameof(items));
            var key = name(item);

            if (string.IsNullOrWhiteSpace(key))
                errors.Add($"{kind} names can't be empty.");
            else if (usedInRules && key[0] is '$' or '@' or '#')
                errors.Add($"{kind} name '{key}' can't be used in rules: names starting with '$', '@' or '#' are reserved for paths.");
            else if (!index.TryAdd(key, item))
                errors.Add($"{kind} '{key}' is registered more than once (names ignore case).");
        }

        return index;
    }
}

/// <summary>
/// A context an event declares, with the provider that loads it.
/// </summary>
/// <param name="Declaration">The event's declaration (name, <c>LoadedBy</c>, <c>ReferencedBy</c>).</param>
/// <param name="Provider">The registered provider that loads it.</param>
public sealed record CatalogContext(ContextDefinition Declaration, ContextProviderRegistration Provider);
