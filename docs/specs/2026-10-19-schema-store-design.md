# Design: Schema Store, Registration and Context References

**Issue:** [#93 Schema store and registration in AddRuleLoom](https://github.com/STotev/RuleLoom/issues/93)
**Parent PRD:** [#87 Schemas and contexts (0.6)](https://github.com/STotev/RuleLoom/issues/87) (user stories 18–21, plus the
context references added in the #92 review)
**Date:** 2026-10-19
**Status:** Approved (AFK)

---

## Problem

Event schemas can be built and exported (#92), but the service that runs the rules has nowhere to keep them, and the
engine doesn't use them. Event schemas also declare how an event refers to each context (`ReferencedBy`); the engine
should resolve that reference and give it to the context provider, so one provider can serve several contexts and
events (e.g. `fromAccount` and `toAccount`) without knowing each event's shape.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Store contract | `ISchemaStore` in `RuleLoom.Schemas`: `GetAsync(eventType)` (case-insensitive), `ListAsync()`, `SaveAsync(schema)` (replaces a schema of the same event type). Async; tenancy and persistence are the implementation's | PRD: storage stays the consumer's |
| In-memory store | `InMemorySchemaStore` (thread-safe), optionally seeded with schemas | Tests, samples and simple services |
| Registration | `AddRuleLoom(r => r.AddSchemas(s => s.Add<OrderPlacedSchema>().AddFromAssemblyOf<T>().AddJson(json)))` registers an `InMemorySchemaStore` (Singleton) seeded with the built and imported schemas; they're built when `AddSchemas` runs, so mistakes surface at startup. Your own store is chosen explicitly with `s.UseStore<MyStore>()` (Scoped); combining it with schemas added in `AddSchemas` throws (save them to your store instead). `SerializerOptions` sets the options for building classes. A later `AddSchemas` call replaces the store | One place to configure RuleLoom, with no dependence on registration order; the DI package references `RuleLoom.Schemas` |
| Where the engine's contract lives | `IContextReferenceSource` in **Abstractions** (`RuleLoom.Abstractions.Context`): `GetReferenceAsync(eventType, contextName)` → `ContextReference?`. `ContextReference` moves from Schemas to Abstractions; `RuleLoom.Schemas` now references `RuleLoom.Abstractions` | The engine (`RuleLoom`) must not depend on `RuleLoom.Schemas`; Abstractions stays dependency-free |
| Schema-backed source | `SchemaStoreContextReferences` (in `RuleLoom.Schemas`) reads the event's schema from the `ISchemaStore`; `AddSchemas` registers it (Scoped, like a tenant-aware store) | |
| Passing the reference | When a context is loaded (prefetch or `GetContextAsync`), the engine asks the source for the context's reference and resolves its paths from the event's current state: a single path gives its value, a composite reference gives an `IReadOnlyDictionary<string, object?>` (part name → value). It's set on `ContextRequest.Reference`. Without a source, a schema or a reference, `Reference` is `null` and the provider reads the subject as before | |
| Typed access | `ContextRequest.GetReference<T>()`: the value converted with `ValueConverter` (single), or the parts mapped onto `T`'s properties by name, ignoring case (composite, e.g. `record ZoneKey(string Country, string Postcode)`) | Providers work with typed keys |
| Wiring | `RuleEngine` and `EvaluationContext` take an optional `IContextReferenceSource`; `AddRuleLoom`'s engine factory resolves it if registered | Existing constructions keep working |

## Example

```csharp
public sealed class TransferSchema : EventSchema<TransferDto>
{
    public TransferSchema()
    {
        Event("TransferMade");
        Context("fromAccount").ReferencedBy(t => t.FromAccountId);
        Context("toAccount").ReferencedBy(t => t.ToAccountId);
    }
}

public sealed class AccountProvider(IAccounts accounts) : ContextProvider<AccountDto>
{
    protected override async ValueTask<AccountDto?> LoadAsync(ContextRequest request, CancellationToken ct) =>
        await accounts.FindAsync(request.GetReference<string>()!, ct);
}

services.AddRuleLoom(r => r
    .AddSchemas(s => s.Add<TransferSchema>())
    .AddContextProvider<AccountProvider>("fromAccount")
    .AddContextProvider<AccountProvider>("toAccount"));
```

## Test plan

- **`InMemorySchemaStore`**: get (case-insensitive), list, save replaces, seeded; an imported JSON collection gives back
  the same schemas.
- **`SchemaStoreContextReferences`**: reference found, unknown event, unknown context, context without a reference.
- **`ContextRequest.GetReference<T>()`**: single value converted, composite mapped onto a record, `null`.
- **Engine**: single and composite references reach providers; one provider under two names gets each its own
  reference; no source / no schema / no reference → `null`; references resolve on JSON and .NET state; on-demand loads
  (`GetContextAsync`) get references too.
- **DI**: `AddSchemas` registers the seeded in-memory store and the source; `UseStore<T>()` registers your store; seeds
  with your own store throw; duplicate event types throw; the engine passes references end to end.

## Docs

- JSON guide (contexts): references and `GetReference<T>()`.
- Quick start / README: `AddSchemas`.
- `AGENTS.md`.
