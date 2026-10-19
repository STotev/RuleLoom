# Design: Context Schemas

**Issue:** [#101 Context schemas described with their providers](https://github.com/STotev/RuleLoom/issues/101)
**Parent PRD:** [#87 Schemas and contexts (0.6)](https://github.com/STotev/RuleLoom/issues/87) (user stories 11–13)
**Date:** 2026-10-19 (reworked 2026-10-20 after review of PR #105)
**Status:** Approved

---

## Problem

Event schemas declare which contexts an event relates to (`Context("toAccount").LoadedBy("account").ReferencedBy(...)`),
but nothing describes a context's fields. The validator (#94) can't check `@toAccount.balance`, and a UI can't offer
"To account › Balance". A context is described once, by the kind of entity a provider loads (`account`), wherever its
data comes from; every event that relates to an account shares that description.

The first version of this slice described contexts in separate `ContextSchema<T>` classes that repeated the provider's
name and type. Review rejected it: the provider already knows both, so it should describe what it loads.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Where contexts are described | In the provider: `ContextProvider<T>` gets an optional `protected virtual void Describe(ContextDescription<T> context)` with `Label`/`Description` and `Field(a => a.Balance)` (`Label`/`Description`/`Ignore()`, as in event schemas) | Type-safe, one place per context; no name or type to repeat |
| Name | The name the provider is registered under (`AddContextProvider<AccountProvider>("account")`, `DirectContextProviderFactory.Add(provider, "account")`); a type registered under several names gets one schema per name | Names belong to the registration, as for actions; event schemas' `LoadedBy("account")` matches it |
| Shape | **Every typed provider has a schema**: fields from how `T` is serialized, with no labels unless `Describe` adds them. Untyped `IContextProvider`s have none | The provider's type declares the shape, so validation and UIs know a context's fields without extra code. Labels stay explicit |
| Collections | A second base class, **`CollectionContextProvider<TItem>`**: `ValueTask<IReadOnlyList<TItem>> LoadAsync(...)` and `Describe(ContextDescription<TItem>)`; its schema is a collection (`IsMany`) whose fields describe one item. `ContextProvider<T>` with a collection `T` fails when its schema is built, pointing to the collection base | C# can't name the item type of `ContextProvider<List<Watcher>>`, so `Field(w => w.Email)` needs the item type as the type parameter |
| Package layering | `RuleLoom.Abstractions` references `RuleLoom.Schemas`. Abstractions holds the contracts the engine runs (operators, actions, providers); Schemas holds descriptions (events, contexts; actions in 0.7). Extension authors reference only Abstractions and describe their extensions with Schemas' builders, which come with it. Schemas still references nothing, so producers use it alone | Provider authoring (e.g. a provider package) stays on Abstractions; no cycle, no extra package. A `Schemas.Abstractions` split can be extracted later if shared contracts grow |
| Reading a provider's schema | `IContextProvider` gets a default interface method `ContextSchemaDefinition? GetSchema(string name, JsonSerializerOptions? options = null) => null`; both base classes implement it by running `Describe` on a `ContextDescription<T>` and building it (`ContextDescription<T>` is public in Schemas, with a public `Build(name, options)`) | No new contract type; untyped providers opt out by default |
| When schemas are read (DI) | At startup: providers are Scoped services with dependencies, so there's no instance while the app is configured. A scope resolves each typed provider once and calls `GetSchema`. The in-memory store is created by a factory with the configured sources plus the provider schemas; your own store gets both from `SchemaSourcesStartup` / `SaveRuleLoomSchemasAsync()`. Provider schemas are only collected when `AddSchemas` is called | Keeps describing in the provider (the reviewed design); errors in descriptions surface at startup (first store use for the in-memory store) instead of in `AddSchemas` |
| Non-DI | `provider.GetSchema("account")` returns the schema; put it in an `InMemorySchemaStore(new SchemaSet(events, contexts))` | No extra API for the rare non-DI setup |
| Model and JSON | `ContextSchemaDefinition` (`Name`, `Label`, `Description`, `IsMany`, `Fields`; `ToJson()`, `Parse(json)`), written as a JSON Schema 2020-12 document with `x-ruleloom-context` (the type is `object`, or `array` with `items` for collections) | Same generator, normalisation and reader as event schemas |
| Schema sets and the store | `SchemaSet` (`Events`, `Contexts`; `ToJson()` one array, `SchemaSet.Parse`) carries both kinds; duplicate event types or context names throw (a provider schema and imported JSON for the same context included). `ISchemaStore`: `GetEventAsync`/`ListEventsAsync`/`SaveEventAsync` and `GetContextAsync`/`ListContextsAsync`/`SaveContextAsync` (context names ignore case) | Exports, imports and the store carry both kinds together, so a catalogue or UI gets everything from one place |
| `SchemaCollection` | Gathers **event** schema classes only (`Add<T>()`, `Add(schema)`, `AddFromAssembly…`) and builds a `SchemaSet`; `EventSchemaDefinition.ParseMany` is replaced by `SchemaSet.Parse` | Contexts come from providers, so there are no context classes to scan |
| Internal structure | The event and context code paths are merged: a shared definition base (document, `Label`, `Description`, `Fields`, `ToJson`), one generator and one reader parameterized by the document kind, `SchemaSet.Parse` dispatching by kind. No behavior change | The first version duplicated both paths |

Schemas and contexts are unreleased, so none of this is a breaking change.

## Example

```csharp
public sealed class AccountProvider(BankDbContext db) : ContextProvider<AccountDto>
{
    protected override async ValueTask<AccountDto?> LoadAsync(ContextRequest request, CancellationToken cancellationToken) =>
        await db.Accounts.FindAsync([request.GetReference<Guid>()], cancellationToken);

    protected override void Describe(ContextDescription<AccountDto> context)
    {
        context.Label("Account");
        context.Field(a => a.Balance).Label("Balance");
        context.Field(a => a.InternalRiskScore).Ignore();
    }
}

public sealed class WatchersProvider : CollectionContextProvider<WatcherDto> { ... }   // a collection context

services.AddRuleLoom(r => r
    .AddSchemas(s => s.AddFromAssemblyOf<TransferMadeSchema>())    // event schemas
    .AddContextProvider<AccountProvider>("account")                // its schema is saved into the store too
    .AddContextProvider<WatchersProvider>("watchers"));
```

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "x-ruleloom-context": "account",
  "title": "Account",
  "type": "object",
  "properties": { "id": { "type": "string" }, "balance": { "type": "number", "title": "Balance" } }
}
```

## Test plan

- **Context description:** shape without `Describe`, labels, `Ignore`, invalid configuration (as for event schemas),
  collection providers (array document, item fields), `ContextProvider<T>` with a collection `T` fails, untyped providers
  have no schema, JSON round trip, malformed documents.
- **`SchemaCollection` / `SchemaSet`:** event schemas from an assembly, duplicates (event types, context names), mixed JSON
  round trip, `Parse` rejects unknown document kinds.
- **Store:** context get (ignoring case), list, save replaces; events unchanged under the new names.
- **DI:** provider schemas (one per registered name) and JSON sources reach the in-memory store and, at startup, your own
  store; providers with scoped dependencies; no schemas collected without `AddSchemas`; a context described twice throws.
- **Refactoring:** the existing event schema tests pass unchanged.

## Docs

- JSON guide (contexts): describing a context in its provider; collection providers.
- Quick start ("Event schemas"): provider description example.
- `AGENTS.md`: architecture table (Abstractions → Schemas; describing vs running), key concepts, extension points.
- Vision ideas: a description pipeline (providers, actions and schema sources contributing descriptions independently),
  to decide with the 0.7 action descriptors.

## Deviations

- `ISchemaStore`'s event methods were renamed in this slice (`GetAsync`/`ListAsync`/`SaveAsync` → `GetEventAsync`/
  `ListEventsAsync`/`SaveEventAsync`) next to the new context methods; `InMemorySchemaStore` takes a `SchemaSet`.
