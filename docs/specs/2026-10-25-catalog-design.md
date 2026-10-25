# Design: The Catalog

**Issue:** to be created (under [PRD #87 Schemas and contexts (0.6)](https://github.com/STotev/RuleLoom/issues/87))
**Date:** 2026-10-25
**Status:** Draft
**Decisions recorded in:** [`GLOSSARY.md`](../../GLOSSARY.md),
[ADR 0001](../adr/0001-catalog-store-is-a-source-code-wins.md),
[ADR 0002](../adr/0002-registration-separate-from-invocation.md)

---

## Problem

What the library knows about an application is spread over six pieces: the operator list, `IActionFactory`,
`IContextProviderFactory`, `ISchemaStore`, `SchemaSet`/`SchemaCollection` and the DI registry (`RuleLoomRegistry`).
Each consumer recombines them:

| Where | What it recombines |
|---|---|
| `RuleEngine` | evaluator (operators), action factory, provider factory, schema store, wrapped by `SchemaReferencingProviders`, which re-reads the event schema from the store on **every context load** to find `LoadedBy`/`ReferencedBy` |
| `RuleValidator` | operators, action factory (for names), provider factory (for names), schema store (async, hence `ValidateAsync`) |
| DI | `RuleLoomRegistry` (types), `SchemaSources` (resolves every provider from a scope to call `GetSchema`), startup sync into the store |
| A future UI | would have to do it a fourth time |

Names exist in two places (the factory and the registry). Provider schemas need a provider **instance**, because
`Describe` is an instance method. Nothing checks the pieces against each other as a whole, so a context declared on an
event with no provider is found only when a rule uses it.

## Scope

Design for **one application**: the event producer, the engine and the rule-authoring UI run in one process. The
service-to-service features (publishing, a reloading catalog) get no design here, but the structure keeps them
**additive** (see [Seams for service-to-service](#seams-for-service-to-service)).

Out of scope:
- Action **parameter** descriptors (next spec; `ActionDescriptor<T>` reserves their place).
- Reloading a catalog at runtime, the history and versioning of saved catalogs, and tenancy beyond "one object per
  catalog".
- Schema publishing, a producer-only DI entry point, consumer-side context-to-provider mapping, and event-type
  namespacing (scopes).

## Decisions

### What a catalog is

| Question | Decision | Rationale |
|---|---|---|
| Contents | Event schemas (with the contexts they declare), context provider registrations (name, type, schema), operator **instances** and action registrations (name, type, label, description) | The vocabulary rules are written in (glossary). Operators are pure, so instances are safe to share. Actions and providers have scoped dependencies, so the catalog holds what they are, not instances |
| Not in it | Rules, settings, `TimeProvider`, runtime state | Rules are written *against* a catalog. Settings are application configuration and must never reach the UI or a store (glossary: Setting) |
| Immutability | Fixed once built: no add or remove afterwards | One consistent view for the engine, the validator and the UI. A change means a new catalog |
| Invariants | Checked by the `Catalog` constructor, not the builder, so no catalog can exist without them (see [Build checks](#build-checks)) | The invariants live with the type, in Abstractions, whoever builds it |
| Tenancy | No feature. The engine and the validator take a catalog as an argument, so a second catalog is just a second object | The only seam, and it costs nothing |
| Placement | `Catalog`, its registration records and the factory contracts in `RuleLoom.Abstractions` (root namespace, like `RulePath`). `CatalogBuilder` in `RuleLoom` (it adds the built-in operators). `CatalogDocument`, `ICatalogStore`, `InMemoryCatalogStore`, `SchemaCollection` and `ContextSchema<T>` in `RuleLoom.Schemas` | Tooling and store packages read catalogs without the engine. A producer only needs `RuleLoom.Schemas` |

### Describing

| Question | Decision | Rationale |
|---|---|---|
| Naming | **Schemas** describe data: `EventSchema<T>` (unchanged) and `ContextSchema<T>` (replaces `ContextDescription<T>`). **Descriptors** describe code: `ActionDescriptor<TAction>`. `RuleDefinition` and `ActionDefinition` keep their names | The split carries meaning: shapes vs behaviour. `ActionDefinition` (a rule's action entry) stays consistent with `RuleDefinition` |
| Style | Abstract base classes configured in their parameterless constructor, like `EventSchema<T>` (and MassTransit's consumer definitions) | The description ships with the type, for example from a package, and needs no instance of the described class, so no scope is needed at build |
| Context schemas | Describe the **data type**: `class CustomerSchema : ContextSchema<Customer>`. A `ContextProvider<Customer>` or `CollectionContextProvider<Customer>` picks it up through its type argument (the base class also gives `IsMany`). With no schema class, the schema is inferred from the type with no labels (today's behaviour). A plain `IContextProvider` has no schema unless the store contributes one | Two providers of `Customer` share one schema. Providers become pure loaders: `Describe` and `GetSchema` are removed |
| Action descriptors | `class SendEmailDescriptor : ActionDescriptor<SendEmailAction>` sets `Label` and `Description`, with parameters in the next spec. With no descriptor, an action has only its name | Packaged actions describe themselves (Q22). The registration still owns the **name** (action-names spec): it can override the name, but not the description |
| Operators | Describe themselves on the contract: `IConditionOperator` gains `Label` and `Description` default members (null), next to `Name` and `MinOperands`. Built-ins get real text | Like the operand counts, the operator states what it is |
| Finding descriptions | `AddAction<TAction>()` and `AddContextProvider<TProvider>(name)` look in the described type's assembly (`TAction`'s or `TProvider`'s) for its descriptor or schema. `AddAction<TAction, TDescriptor>()` and `AddContextProvider<TProvider, TSchema>(name)` give it explicitly. `AddFromAssemblyOf<T>()` registers the event schemas and actions (default names) in an assembly, and the descriptors and context schemas there. Two descriptors or schemas for one type fail the build | A package ships both and users write one line, with an override when needed. Providers aren't registered by a scan because each needs a name |
| Serializer options | `CatalogBuilder.UseJsonOptions(options)` applies to every event and context schema, with Web defaults. Schemas are generated in `Build`, so the call order doesn't matter | Field names come from how DTOs serialize. Not coupled to ASP.NET Core's `JsonOptions`: the rules' paths must match how events are actually serialized, and making that explicit is the app's call |

### Building and the store

| Question | Decision | Rationale |
|---|---|---|
| Two builds | `Build()` uses code registrations only and does no I/O. `BuildAsync(ICatalogStore store, ct)` loads the store, builds (merge plus checks) and saves the result | No hidden I/O. Tests and simple apps call `Build()` |
| Store is a source | The store contributes **event and context schemas** that no code defines. Operators and actions come only from code, and their stored entries are ignored on load | ADR 0001: a store is a store, whatever the deployment. Without code, operators and actions can't run |
| Conflicts | **Code wins** (by kind and name), and the code version is written back | A stored schema that contradicts code is developer error. Code is what the app actually raises |
| When it's read | Only by `BuildAsync`. A stored change takes effect in the next catalog, which today means on restart | The catalog stays immutable. Reload comes later (see [Seams](#seams-for-service-to-service)) |
| Store contract | `LoadAsync` returns a `CatalogDocument` (empty if nothing is stored). `SaveAsync(CatalogDocument)` **upserts each entry** by kind and name and **never deletes** | Concurrent writers (scaled-out instances, later publishing producers) only conflict per entry, and the last write wins. A partial document is safe to save |
| Removed code entries | An event type deleted from code stays in the store, and so loads as a store-contributed event (harmless: nothing raises it). Removing stored entries is a later, explicit operation | The consequence of never deleting on save |
| Problems aren't stored | `SaveAsync` ignores `Problems`; they are recomputed by every build | Problems describe a build, not the store |
| One document | `CatalogDocument` serves as the store's format, the UI's input (`catalog.ToJson()`) and, later, the publish payload. Every section is optional | One format to keep compatible |

### Build checks

Broken links in **code-defined** entries throw `InvalidOperationException` from the `Catalog` constructor (and so from
`Build`/`BuildAsync`), listing **every** problem in one message. They are this application's mistakes and must stop it
from starting:

1. Two entries of one kind with the same name, compared case-insensitively. The kinds are event types, context
   providers, operators and actions.
2. An event declares a context whose provider (`LoadedBy`, or else the context name) isn't registered.
3. An event declares a context whose `ReferencedBy` path isn't a field of the event schema.
4. An operator or action name that rule JSON can't use: empty, or starting with `$`, `@` or `#`.
5. A context schema type that doesn't match its provider's type (`AddContextProvider<TProvider, TSchema>` with the
   wrong `T`).

The same links broken in **store-contributed** entries don't throw. The entry is kept and recorded as a
**catalog problem** (`Catalog.Problems`, also written to the document for the UI). This covers checks 2 and 3 on a
stored event, and a stored context schema with no registered provider. An outside write can never stop the application
from starting (ADR 0001). The problem affects only the rules that use the entry:
- **Validation**: a rule on that event, or one using that context, gets an error that names the problem.
- **Runtime**: that context fails to load, exactly like a provider failure today (`ContextLoadException` when it's
  resolved), so only the rules that use it fail.

These are **not** problems: a provider no event declares, an action or operator no rule uses, or a provider with no
schema.

### Running on a catalog

| Question | Decision | Rationale |
|---|---|---|
| Registration vs invocation | The engine looks a name up in the catalog and gives the factory the **registration**: `IActionFactory.Create(ActionRegistration)` and `IContextProviderFactory.Create(ContextProviderRegistration)`. Factories never resolve names, and they no longer list them (`ActionNames` and `ProviderNames` are removed) | ADR 0002. The catalog is the only source of truth for names, and any container plugs in through one narrow contract per kind |
| Default factories | `DirectActionFactory` and `DirectContextProviderFactory` hold instances added by the caller, keyed by type, and otherwise create the registered type with its parameterless constructor | Covers non-DI use with no new types |
| Engine | `new RuleEngine(catalog, actions?, contextProviders?, timeProvider?)`, with the factories defaulting to the direct ones. It evaluates conditions with `new ConditionEvaluator(catalog)` (operators looked up in the catalog). The schema-store parameter and `SchemaReferencingProviders` are removed: the engine finds the context's declaration and provider through the catalog and resolves `ContextRequest.Reference` itself | The link is resolved once, at build, instead of on every load |
| Unknown event types | Execute normally. The catalog describes; it doesn't gate execution | A missing registration never takes production down |
| Undeclared contexts | A context is found only through its event's declaration. A rule using `@x` on an event that doesn't declare `x` (or on an unknown event) fails like a failed context load. There's **no fallback** to a provider with the same name | The declaration's `ReferencedBy` tells the provider *which* record to load, so without one the context usually only worked by accident. **Behaviour change**: contexts now need an event schema that declares them |
| Validator | `new RuleValidator(catalog)`, with one synchronous `Validate` that includes the schema checks. `ValidateAsync` is removed (it only existed because the store was async). New errors: an event type not in the catalog, a context its event doesn't declare, and an entry with a catalog problem. **Behaviour change**: validating a rule now needs its event type in the catalog (before, rules for events without a schema skipped the path checks) | Strict authoring, permissive execution. The UI only offers what's registered anyway |

### Dependency injection

| Question | Decision | Rationale |
|---|---|---|
| Shape | `services.AddRuleLoom(o => o.AddCatalog(c => c.AddEvent<...>().AddAction<...>()).UseStore<TStore>())`. `AddCatalog` configures one shared `CatalogBuilder` (repeated calls compose, as `AddRuleLoom` does today). `UseStore<TStore>()` registers the store as Scoped (stores over a `DbContext`). `UseStore(instance)` registers one instance, which is what an in-memory store needs to keep its contents | The same builder API with or without DI. A delegate rather than a builder instance, so that repeated `AddRuleLoom` calls (for example from packages) configure the same builder |
| What moves | `AddAction`, `AddContextProvider`, `AddOperator`, `RemoveOperator`, `RemoveBuiltInOperators` and `AddSchemas` leave `RuleLoomBuilder`. They're on `CatalogBuilder`. `RuleLoomBuilder` keeps `Services`, `AddCatalog` and `UseStore` | One place to register. Packages extend `CatalogBuilder` (for example `AddMandrill()`), and their settings are ordinary `services.Configure<MandrillSettings>(...)` |
| Creating instances | `ServiceProviderActionFactory` and `ServiceProviderContextProviderFactory` (Scoped) use `ActivatorUtilities.GetServiceOrCreateInstance(scope, registration.Type)`. A type registered in `services` is used as registered, and otherwise it's created with its dependencies from the scope | Nothing to register twice. **Caveat** (documented): instances that aren't registered aren't disposed by the scope, so register disposable actions and providers in `services` yourself |
| Operators | Parameterless or added as instances (`AddOperator<T>()` / `AddOperator(instance)`), with no DI-created operators | Operators are pure functions of their operands, and the catalog can be built without a container |
| When it's built | A hosted service (`CatalogStartup`) builds the catalog at startup, with `BuildAsync` using the store resolved from a scope when there is one, and `Build()` otherwise. Without a host, `await serviceProvider.StartRuleLoomAsync()` (which replaces `SaveRuleLoomSchemasAsync`) does the same | A broken catalog fails host startup rather than the first event |
| Resolving | The built catalog sits in an internal holder (Singleton). `Catalog` is registered **Scoped** from `holder.Current`. Resolving it before startup throws `InvalidOperationException("RuleLoom hasn't started: ...")`. `RuleEngine` and `RuleValidator` stay Scoped. `IConditionEvaluator` is no longer registered | Each scope sees one consistent snapshot, and reloading later only swaps `Current` (see [Seams](#seams-for-service-to-service)) |

### Seams for service-to-service

These keep the later producer/consumer setup additive. None of them adds behaviour now.

| Later need | Seam taken now |
|---|---|
| A producer registers only event schemas, without the engine | `SchemaCollection`, `CatalogDocument` and `ICatalogStore` live in `RuleLoom.Schemas`. `SchemaCollection.Build()` returns a `CatalogDocument` with only `Events` |
| Producers publish schemas into the consumer's store | Upsert-per-entry saves and optional document sections. The publish payload is a `CatalogDocument` |
| The consumer accepts schemas at runtime | DI resolves `Catalog` per scope from a holder. A reload swaps the holder's catalog, and the engine and validator don't change |
| Another team's schema is broken | Store-contributed entries become catalog problems and never fail the build |

## Contracts

```csharp
// RuleLoom.Schemas

public abstract class ContextSchema<T> : IContextSchema            // replaces ContextDescription<T>
{
    protected ContextSchema<T> Label(string label);
    protected ContextSchema<T> Description(string description);
    protected FieldBuilder Field<TProperty>(Expression<Func<T, TProperty>> property);
    public ContextSchemaDefinition Build(string name, bool isMany = false, JsonSerializerOptions? options = null);
}

public interface IContextSchema                                      // lets the builder build any ContextSchema<T>
{
    Type DataType { get; }
    ContextSchemaDefinition Build(string name, bool isMany = false, JsonSerializerOptions? options = null);
}

public sealed record CatalogOperator(string Name, string? Label, string? Description, int MinOperands, int? MaxOperands);   // null = unlimited
public sealed record CatalogAction(string Name, string? Label, string? Description);   // parameters: next spec
public enum CatalogEntryKind { Event, Context }
public sealed record CatalogProblem(CatalogEntryKind Kind, string Name, string Message);

public sealed class CatalogDocument
{
    public CatalogDocument(
        IEnumerable<EventSchemaDefinition>? events = null,
        IEnumerable<ContextSchemaDefinition>? contexts = null,
        IEnumerable<CatalogOperator>? operators = null,
        IEnumerable<CatalogAction>? actions = null,
        IEnumerable<CatalogProblem>? problems = null);              // duplicate names per kind throw

    public IReadOnlyList<EventSchemaDefinition> Events { get; }
    public IReadOnlyList<ContextSchemaDefinition> Contexts { get; }
    public IReadOnlyList<CatalogOperator> Operators { get; }
    public IReadOnlyList<CatalogAction> Actions { get; }
    public IReadOnlyList<CatalogProblem> Problems { get; }

    public string ToJson();
    public static CatalogDocument Parse(string json);               // JsonException on malformed documents
}

public interface ICatalogStore
{
    ValueTask<CatalogDocument> LoadAsync(CancellationToken cancellationToken = default);
    ValueTask SaveAsync(CatalogDocument document, CancellationToken cancellationToken = default);   // upsert, never delete
}

public sealed class InMemoryCatalogStore : ICatalogStore;

public sealed class SchemaCollection(JsonSerializerOptions? options = null)   // kept; Build() changes type
{
    public SchemaCollection Add<TSchema>() where TSchema : IEventSchema, new();
    public SchemaCollection Add(IEventSchema schema);
    public SchemaCollection AddFromAssembly(Assembly assembly);
    public SchemaCollection AddFromAssemblyOf<T>();
    public CatalogDocument Build();                                 // events only
}
```

```csharp
// RuleLoom.Abstractions

public sealed record ActionRegistration(string Name, Type Type, string? Label, string? Description);
public sealed record ContextProviderRegistration(string Name, Type Type, ContextSchemaDefinition? Schema);

public sealed class Catalog
{
    public Catalog(
        IEnumerable<EventSchemaDefinition> events,
        IEnumerable<ContextProviderRegistration> contextProviders,
        IEnumerable<IConditionOperator> operators,
        IEnumerable<ActionRegistration> actions,
        CatalogDocument? stored = null);    // merges (code wins), checks; throws for code-defined problems

    public IReadOnlyList<EventSchemaDefinition> Events { get; }     // code and stored, merged
    public IReadOnlyList<ContextProviderRegistration> ContextProviders { get; }   // schemas merged in
    public IReadOnlyList<IConditionOperator> Operators { get; }
    public IReadOnlyList<ActionRegistration> Actions { get; }
    public IReadOnlyList<CatalogProblem> Problems { get; }

    public bool TryGetEvent(string eventType, [NotNullWhen(true)] out EventSchemaDefinition? schema);
    public bool TryGetContextProvider(string name, [NotNullWhen(true)] out ContextProviderRegistration? provider);
    public bool TryGetOperator(string name, [NotNullWhen(true)] out IConditionOperator? @operator);
    public bool TryGetAction(string name, [NotNullWhen(true)] out ActionRegistration? action);

    // The event's declaration and its provider; false when undeclared, unknown, or a catalog problem (with the reason).
    public bool TryGetContext(string eventType, string name,
        [NotNullWhen(true)] out CatalogContext? context, [NotNullWhen(false)] out string? error);

    public CatalogDocument ToDocument();
    public string ToJson();                                          // ToDocument().ToJson()
}

public sealed record CatalogContext(ContextDefinition Declaration, ContextProviderRegistration Provider);

public interface IActionFactory { IAction Create(ActionRegistration action); }
public interface IContextProviderFactory { IContextProvider Create(ContextProviderRegistration provider); }

public interface IActionDescriptor                                  // lets the builder read any ActionDescriptor<T>
{
    Type ActionType { get; }
    string? Label { get; }
    string? Description { get; }
}

public abstract class ActionDescriptor<TAction> : IActionDescriptor where TAction : IAction
{
    protected ActionDescriptor<TAction> Label(string label);
    protected ActionDescriptor<TAction> Description(string description);
}

public interface IConditionOperator
{
    // ...unchanged members...
    string? Label => null;
    string? Description => null;
}

public interface IContextProvider
{
    ValueTask<object?> GetAsync(ContextRequest request, CancellationToken cancellationToken);   // GetSchema removed
}
// ContextProvider<T> / CollectionContextProvider<T>: Describe and GetSchema removed.
```

```csharp
// RuleLoom

public sealed class CatalogBuilder
{
    public CatalogBuilder();                                         // starts with the built-in operators
    public CatalogBuilder UseJsonOptions(JsonSerializerOptions options);
    public CatalogBuilder AddEvent<TSchema>() where TSchema : IEventSchema, new();
    public CatalogBuilder AddFromAssemblyOf<T>();
    public CatalogBuilder AddContextProvider<TProvider>(string name) where TProvider : class, IContextProvider;
    public CatalogBuilder AddContextProvider<TProvider, TSchema>(string name)
        where TProvider : class, IContextProvider where TSchema : IContextSchema, new();
    public CatalogBuilder AddAction<TAction>(string? name = null) where TAction : class, IAction;
    public CatalogBuilder AddAction<TAction, TDescriptor>(string? name = null)
        where TAction : class, IAction where TDescriptor : ActionDescriptor<TAction>, new();
    public CatalogBuilder AddOperator<TOperator>() where TOperator : IConditionOperator, new();
    public CatalogBuilder AddOperator(IConditionOperator @operator);
    public CatalogBuilder RemoveOperator(string name);
    public CatalogBuilder RemoveBuiltInOperators();

    public Catalog Build();
    public Task<Catalog> BuildAsync(ICatalogStore store, CancellationToken cancellationToken = default);
}

public sealed class RuleEngine(
    Catalog catalog,
    IActionFactory? actions = null,
    IContextProviderFactory? contextProviders = null,
    TimeProvider? timeProvider = null);                              // ExecuteAsync overloads unchanged

public sealed class RuleValidator(Catalog catalog)
{
    public RuleValidationResult Validate(RuleDefinition rule);       // ValidateAsync removed
}

public sealed class ConditionEvaluator(Catalog catalog) : IConditionEvaluator;
public sealed class DirectActionFactory : IActionFactory { public DirectActionFactory Add(IAction action); }
public sealed class DirectContextProviderFactory : IContextProviderFactory
{
    public DirectContextProviderFactory Add(IContextProvider provider);
}
```

```csharp
// RuleLoom.Extensions.DependencyInjection

public sealed class RuleLoomBuilder
{
    public IServiceCollection Services { get; }
    public RuleLoomBuilder AddCatalog(Action<CatalogBuilder> configure);
    public RuleLoomBuilder UseStore<TStore>() where TStore : class, ICatalogStore;   // Scoped
    public RuleLoomBuilder UseStore(ICatalogStore store);                            // this instance (InMemoryCatalogStore)
}

public static class RuleLoomServiceProviderExtensions
{
    public static Task StartRuleLoomAsync(this IServiceProvider services, CancellationToken cancellationToken = default);
}
```

**Removed:** `ISchemaStore`, `InMemorySchemaStore`, `SchemaSet`, `ContextDescription<T>`, `IContextProvider.GetSchema`,
`ContextProvider<T>.Describe`, `IActionFactory.TryGetAction`/`ActionNames`, `IContextProviderFactory.TryGetProvider`/
`ProviderNames`, `SchemaReferencingProviders`, `RuleLoomRegistry`, `SchemaSources`, `SchemaSourcesStartup`,
`SchemaRegistration`, `SaveRuleLoomSchemasAsync`, `RuleValidator.ValidateAsync`, and the `ConditionEvaluator` and
`RuleValidator` constructors that take operator lists.

**Breaking for released API (0.5):** `IActionFactory`, `DirectActionFactory`, `RuleEngine`'s and `RuleValidator`'s
constructors, `ConditionEvaluator`'s constructor, and the methods that moved from `RuleLoomBuilder` to
`CatalogBuilder`. The PR title is `feat!:`, and the migration goes in [`guides/upgrading.md`](../guides/upgrading.md).
Everything else above is unreleased 0.6 work.

## Example

```csharp
public sealed class OrderPlacedSchema : EventSchema<OrderPlaced>
{
    public OrderPlacedSchema()
    {
        Event("order-placed").Label("Order placed");
        Context("customer").ReferencedBy(o => o.CustomerId);
    }
}

public sealed class CustomerSchema : ContextSchema<Customer>
{
    public CustomerSchema()
    {
        Label("Customer");
        Field(c => c.Tier).Label("Loyalty tier");
    }
}

public sealed class SendEmailDescriptor : ActionDescriptor<SendEmailAction>
{
    public SendEmailDescriptor() => Label("Send email").Description("Sends a templated email.");
}

// Without DI
var catalog = new CatalogBuilder()
    .AddEvent<OrderPlacedSchema>()
    .AddContextProvider<CustomerProvider>("customer")               // finds CustomerSchema through Customer
    .AddAction<SendEmailAction>()                                   // finds SendEmailDescriptor
    .Build();

var engine = new RuleEngine(catalog, new DirectActionFactory().Add(new SendEmailAction(mailer)));
var validator = new RuleValidator(catalog);
var forTheUi = catalog.ToJson();

// With DI
services.Configure<MandrillSettings>(configuration.GetSection("Mandrill"));
services.AddRuleLoom(o => o
    .AddCatalog(c => c.AddFromAssemblyOf<OrderPlacedSchema>().AddContextProvider<CustomerProvider>("customer").AddMandrill())
    .UseStore<PostgresCatalogStore>());
```

## Test plan

- **Catalog** (unit): each build check throws for code-defined entries with all problems in one message; the same
  broken links in stored entries become `Problems`; code wins over stored entries of the same name; stored operators and
  actions are ignored; `TryGetContext` resolves `LoadedBy`, the default provider name, undeclared contexts and contexts
  with problems; `ToDocument` round-trips through `ToJson`/`Parse`.
- **CatalogBuilder**: descriptor and context-schema discovery (next to the type, explicit, by scan, duplicates);
  `UseJsonOptions` applies regardless of order; `BuildAsync` loads, merges and saves; `Build` never touches a store.
- **InMemoryCatalogStore**: upsert per entry; nothing is ever deleted; problems aren't stored.
- **Engine**: actions and providers are created through the factories from registrations; references come from the
  catalog's declaration; undeclared and unknown-event contexts fail only the rules that use them; unknown event types
  still execute; the existing engine tests are ported to catalogs.
- **Validator**: unknown event type, undeclared context and a catalog problem are errors; the existing schema-path tests
  are ported (now synchronous).
- **DI** (real container): startup builds the catalog with and without a store; a broken catalog fails `StartAsync`;
  resolving before startup throws; `Catalog` is per scope; factories use registered services when present; repeated
  `AddRuleLoom`/`AddCatalog` calls compose.
- **Integration**: the samples run on a catalog end to end.

## Docs

- `AGENTS.md`: architecture table, the execution flow (names through the catalog), Key concepts (catalog, schemas vs
  descriptors, store), and Extension points (descriptors; providers without `Describe`).
- `guides/upgrading.md`: migration from 0.5 (factories, engine, validator, DI builder methods).
- `src/PACKAGE.md` and `README.md` quick starts.
- `docs/roadmap.md`: this spec in 0.6; the 0.7 "catalogue" folds in here, and action parameter descriptors stay for 0.7.
- `GLOSSARY.md` and the ADRs already record the language and the two hard-to-reverse decisions.

## Deviations

None yet.
