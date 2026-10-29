using System.Text.Json.Nodes;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Context;
using RuleLoom.Abstractions.Values;
using RuleLoom.Engine;
using RuleLoom.Operators;
using RuleLoom.Schemas;

namespace RuleLoom.Tests;

/// <summary>
/// A catalog for tests, built from instances: actions and providers are registered by name and created by name-keyed
/// factories (as a container-backed factory could), so several instances of one type can sit under different names.
/// </summary>
internal sealed class TestCatalog : IActionFactory, IContextProviderFactory
{
    private readonly Dictionary<string, IAction> _actions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (IContextProvider Provider, ContextSchemaDefinition? Schema)> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<EventSchemaDefinition> _events = [];
    private readonly List<IConditionOperator> _operators = [.. BuiltInOperators.Create()];
    private CatalogDocument? _stored;
    private TemporalRules? _temporalRules;

    public TestCatalog Action(string name, IAction action)
    {
        _actions.Add(name, action);
        return this;
    }

    public TestCatalog Provider(string name, IContextProvider provider, ContextSchemaDefinition? schema = null)
    {
        _providers.Add(name, (provider, schema));
        return this;
    }

    public TestCatalog Event(EventSchemaDefinition schema)
    {
        _events.Add(schema);
        return this;
    }

    /// <summary>Adds an event without fields that declares <paramref name="contexts"/>, each loaded by the provider of the same name.</summary>
    public TestCatalog Event(string eventType, params string[] contexts) => Event(Schema(eventType, contexts));

    /// <summary>What a catalog store holds: its entries' broken links become problems instead of failing the build.</summary>
    public TestCatalog Stored(CatalogDocument document)
    {
        _stored = document;
        return this;
    }

    /// <summary>Reads date-times without an offset in <paramref name="timeZoneId"/>.</summary>
    public TestCatalog TimeZone(string timeZoneId)
    {
        _temporalRules = new TemporalRules(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        return this;
    }

    public TestCatalog Operators(params IConditionOperator[] operators)
    {
        _operators.Clear();
        _operators.AddRange(operators);
        return this;
    }

    public Catalog Build() => new(
        _events,
        _providers.Select(p => new ContextProviderRegistration(p.Key, p.Value.Provider.GetType(), p.Value.Schema)),
        _operators,
        _actions.Select(a => new ActionRegistration(a.Key, a.Value.GetType())),
        _stored,
        _temporalRules);

    public RuleEngine Engine(TimeProvider? timeProvider = null) => new(Build(), this, this, timeProvider);

    public EvaluationContext Context(object? current, object? previous, string eventType) =>
        new(current, previous, eventType, Build(), this);

    IAction IActionFactory.Create(ActionRegistration action) => _actions[action.Name];

    IContextProvider IContextProviderFactory.Create(ContextProviderRegistration provider) => _providers[provider.Name].Provider;

    public static EventSchemaDefinition Schema(string eventType, params string[] contexts) => EventSchemaDefinition.Parse(new JsonObject
    {
        ["x-ruleloom-event"] = eventType,
        ["type"] = "object",
        ["x-ruleloom-contexts"] = new JsonObject(contexts.Select(name => KeyValuePair.Create(name, (JsonNode?)new JsonObject())))
    }.ToJsonString());
}
