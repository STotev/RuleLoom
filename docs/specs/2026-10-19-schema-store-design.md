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
| Registration | `AddRuleLoom(r => r.AddSchemas(s => ...))` separates **sources** from **storage**. Sources: `Add<TSchema>()`, `AddFromAssemblyOf<T>()`, `AddJson(json)`, built when `AddSchemas` runs so mistakes surface at startup; duplicates throw. Storage: the `InMemorySchemaStore` (Singleton) by default, or `UseStore<MyStore>()` (Scoped). Sources are saved into the store: the in-memory store starts with them; your own store gets them at startup through a hosted service (`IHostedService`, adds `Microsoft.Extensions.Hosting.Abstractions` to the DI package), which calls `serviceProvider.SaveRuleLoomSchemasAsync()`. Apps without a host call that themselves. Saving replaces stored schemas of the same event type (code is the source of truth) and runs in its own scope, so a tenant-aware store sees them as shared. `SerializerOptions` sets the options for building classes. A later `AddSchemas` call replaces the configuration | One place to configure RuleLoom, no dependence on registration order, and `Add…` keeps one meaning whichever store is used (publishing and storage packages in 0.7 add stores and destinations, not new semantics) |
| How the engine gets references | **The engine (`RuleLoom`) references `RuleLoom.Schemas`** and takes an optional `ISchemaStore`. It wraps the context providers it hands to `EvaluationContext`; before calling a provider, the wrapper reads the event's schema, resolves the context's `ReferencedBy` and passes the request on with `Reference` set. Abstractions only gains `ContextRequest.Reference` and `GetReference<T>()`; `ContextReference` stays in Schemas, which stays dependency-free | Decided in review: a seam interface in Abstractions (tried first) existed only to avoid a dependency that should exist. `RuleLoom.Schemas` still ships separately and comes with `RuleLoom` transitively |
| Passing the reference | When a context is loaded (prefetch or `GetContextAsync`), the engine asks the source for the context's reference and resolves its paths from the event's current state: a single path gives its value, a composite reference gives an `IReadOnlyDictionary<string, object?>` (part name → value). It's set on `ContextRequest.Reference`. Without a source, a schema or a reference, `Reference` is `null` and the provider reads the subject as before | |
| Provider names | A schema context can name the provider that loads it: `Context("fromAccount").LoadedBy("account")` (`x-ruleloom-provider`, `ContextDefinition.Provider`, defaulting to the context's name). With schemas, the engine finds the provider through the event's schema when the context loads; a context whose provider isn't registered fails its rules with a clear error. Without schemas, the context name is the provider name | Added in review: register a provider once per kind of entity (`account`) instead of once per usage; rules keep distinct context names (`@fromAccount`, `@toAccount`); #101 describes shapes per provider, so both share `account`'s fields |
| Typed access | `ContextRequest.GetReference<T>()`: the value converted with `ValueConverter` (single), or the parts mapped onto `T`'s properties by name, ignoring case (composite, e.g. `record ZoneKey(string Country, string Postcode)`) | Providers work with typed keys |
| Wiring | `RuleEngine` takes an optional `ISchemaStore`; `AddRuleLoom`'s engine factory resolves it if registered (`AddSchemas`). Manual use: `new RuleEngine(evaluator, actions, contextProviders: providers, schemas: store)` | Existing constructions keep working; no DI or ASP.NET Core needed |

## Example

```csharp
public sealed class TransferSchema : EventSchema<TransferDto>
{
    public TransferSchema()
    {
        Event("TransferMade");
        Context("fromAccount").LoadedBy("account").ReferencedBy(t => t.FromAccountId);
        Context("toAccount").LoadedBy("account").ReferencedBy(t => t.ToAccountId);
    }
}

public sealed class AccountProvider(IAccounts accounts) : ContextProvider<AccountDto>
{
    protected override async ValueTask<AccountDto?> LoadAsync(ContextRequest request, CancellationToken ct) =>
        await accounts.FindAsync(request.GetReference<string>()!, ct);
}

services.AddRuleLoom(r => r
    .AddSchemas(s => s.Add<TransferSchema>())
    .AddContextProvider<AccountProvider>("account"));
```

## Test plan

- **`InMemorySchemaStore`**: get (case-insensitive), list, save replaces, seeded; an imported JSON collection gives back
  the same schemas.
- **`ContextRequest.GetReference<T>()`**: single value converted, composite mapped onto a record, `null`.
- **Engine**: single and composite references reach providers; one provider under two names gets each its own
  reference; no schemas / unknown event / no declared reference → `null`; references resolve on JSON and .NET state; on-demand loads
  (`GetContextAsync`) get references too.
- **DI**: `AddSchemas` registers the in-memory store with the sources; `UseStore<T>()` registers your store and the
  sources are saved into it by `SaveRuleLoomSchemasAsync()` and by the hosted service; no sources → nothing saved;
  duplicate event types throw; the engine passes references end to end.

## Docs

- JSON guide (contexts): references and `GetReference<T>()`.
- Quick start / README: `AddSchemas`.
- `AGENTS.md`.

## Deviations

- The engine's references came first through an `IContextReferenceSource` seam in Abstractions; in review the core
  referencing `RuleLoom.Schemas` was preferred, so the seam is gone and the engine reads `ISchemaStore` directly (wrapping
  providers), and `ContextReference` stays in Schemas.
- The wider storage model (sources / storage / publishing; RuleLoom-provided storage) was decided in review and recorded in
  the vision; publishing, registration endpoints, startup saving into persistent stores and storage packages are 0.7.
- Sources and storage were separated in review (they were first exclusive: sources only seeded the in-memory store);
  sources are now saved into any store at startup (hosted service or `SaveRuleLoomSchemasAsync()`), so `Add…` keeps one
  meaning when 0.7 adds stores.
- Context names and provider names were separated (`LoadedBy`), so one provider serves several contexts.
- `GetReference<string>()` converts any value to its invariant text and `GetReference<Guid>()` parses GUID text, beyond
  the shared value converter (common for IDs).
