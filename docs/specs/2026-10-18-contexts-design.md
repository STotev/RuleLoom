# Design: Contexts — Related Data Loaded by Context Providers

**Issue:** [#91 Contexts: related data loaded by context providers (@ paths)](https://github.com/STotev/RuleLoom/issues/91)
**Parent PRD:** [#87 Schemas and contexts (0.6)](https://github.com/STotev/RuleLoom/issues/87) (user stories 33–48, 56)
**Date:** 2026-10-18
**Status:** Approved (AFK)

---

## Problem

Rules often need data beyond the event's subject: "an order is placed **and** the customer is Gold", "a car ad's price
dropped: email **all its watchers**". Today the producer has to put all of it into `current` (and `previous`), which
means shipping whole aggregates with every event. Related data has no previous state worth comparing, so it can be
loaded where the rules run, and only when a rule needs it.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Path syntax | `@name` (the whole context) and `@name.Path`. The name is the first segment after `@`; names ignore case | PRD: `$` is RuleLoom's, `@` is the consumer's, `#` is reserved for functions |
| Parser | Rejects paths starting with `#` (reserved for functions), an empty context name (`@` or `@.X`), and `@` right after `$current.`/`$previous.` (contexts have no previous state). `@` elsewhere in a path (`Payload.@type`) stays a plain key | Errors at parse time, with the path's JSONPath, like unknown `$` variables. JSON keys such as `@type` keep working |
| Provider contract (Abstractions, `RuleLoom.Abstractions.Context`) | `IContextProvider.GetAsync(ContextRequest request, CancellationToken)` returns `ValueTask<object?>`: a .NET object, JSON, a collection, or `null` for "not found". `ContextRequest` carries `Name`, `EventType`, `Current`, `Previous` and `Resolve(path)` (subject paths only) | The provider decides how to find the context (e.g. `request.Resolve("CustomerId")`), so contexts work without schemas |
| Typed providers | `ContextProvider<TContext>`: an abstract base class implementing `IContextProvider` with a typed `LoadAsync`. The engine needs no type information (paths resolve on the runtime value); the type documents the provider and can be read from the provider type later (schemas, catalogue) | Added in review: type safety for provider authors without changing the engine's contract. A typed *subject* isn't offered: when events come from other services the subject is JSON, without the producer's classes |
| Registry | `IContextProviderFactory` (`TryGetProvider(name, out provider)`, `ProviderNames`), mirroring `IActionFactory`. Non-DI: `DirectContextProviderFactory` (`RuleLoom.Contexts`) with `Add(provider, name)`. DI: `AddContextProvider<T>(name)` on `RuleLoomBuilder` (provider Scoped; `ServiceProviderContextProviderFactory` Scoped). Names are case-insensitive; duplicates throw | Same shape and lifetimes as actions: providers often use scoped services (HTTP clients, `DbContext`) |
| Engine wiring | `RuleEngine` gets an optional `IContextProviderFactory` (last constructor parameter; `AddRuleLoom` passes the scoped one). Without it, `@` paths fail the rule | Existing constructions keep compiling |
| Where contexts live | `EvaluationContext` gets a constructor taking the event type and an `IContextProviderFactory`, and `GetContextAsync(name, ct)`. Each name is loaded at most once per evaluation context (a cached task) | One cache shared by prefetch, conditions and actions |
| Prefetch | Before evaluating, the engine collects `@` names used by the event's matching rules: paths in rule conditions, action conditions and schedule `referencePath`s. It loads them in parallel through `GetContextAsync` and waits for all; failures are kept in the cache, not thrown | Path resolution stays synchronous; contexts not used by any rule are never loaded |
| `Resolve("@name…")` | Uses the loaded value and resolves the rest of the path on it like any state (objects, JSON, or `null`). A failed load throws `ContextLoadException` (the provider's exception as inner); an unregistered name, or a name that wasn't loaded (e.g. a custom operator building a path the scan couldn't see), throws `InvalidOperationException` | A throwing condition already fails just that rule (or action), so a failed context fails only the rules that use it |
| Actions | `await context.GetContextAsync("watchers", ct)` loads any registered context on demand through the same cache; prefetched contexts come back immediately. `GetContextAsync<T>` returns it typed: as is when it's already a `T`, deserialized from JSON (property names ignoring case, web defaults), `InvalidCastException` otherwise | Actions can use contexts the conditions don't mention, without casting; providers that call other services often return JSON |
| Collections | A context is whatever the provider returns; a collection comes back as is (e.g. a list of watchers). Paths into a collection resolve to `null` (no projection, as with arrays) | `any`/`all` and projections are out of scope (PRD) |
| `changed` on a context | Throws `InvalidOperationException` ("contexts have no previous state"), failing the rule | `changed @customer.Tier` can't be meaningful |
| Cancellation | The token passed to `ExecuteAsync` (or `GetContextAsync`) reaches providers; `OperationCanceledException` propagates as today | |

## Flow

```
ExecuteAsync(eventType, current, previous, rules, ct)
  → matching rules (event type), ordered by priority
  → names = @-names in their conditions, action conditions and schedule reference paths
  → await all GetContextAsync(name, ct) (failures cached, cancellation rethrown)
  → evaluate rules as today; Resolve("@name.Path") reads the cache
```

## Example

```csharp
public sealed class CustomerProvider(ICustomersClient customers) : ContextProvider<CustomerDto>
{
    protected override async ValueTask<CustomerDto?> LoadAsync(ContextRequest request, CancellationToken cancellationToken) =>
        request.Resolve("CustomerId") is string id ? await customers.FindAsync(id, cancellationToken) : null;
}

services.AddRuleLoom(rules => rules.AddContextProvider<CustomerProvider>("customer"));
```

```json
{ "operator": "eq", "operands": [{ "path": "@customer.Tier" }, { "value": "Gold" }] }
```

## Test plan

- **Parser** (`RuleJsonValidationTests`): `#now`, `@`, `@.Tier`, `$current.@customer`, `$previous.@customer.Tier`
  rejected with paths; `@customer.Tier` and `Payload.@type` accepted.
- **`EvaluationContext`** (`ContextTests`):
  - `GetContextAsync` loads once and caches;
  - `Resolve("@name.Path")` on objects, JSON and collections;
  - `null` means not found;
  - failed load → `ContextLoadException`; unregistered and not-loaded names → `InvalidOperationException`;
  - cancellation;
  - `ContextProvider<T>`; `GetContextAsync<T>` (same type, JSON element/node deserialized ignoring case, not found, other type).
- **Engine** (`RuleEngineContextTests`, fake providers counting calls):
  - only used contexts are loaded, once each, even when several rules use them;
  - loads run in parallel;
  - action conditions and schedule `referencePath` see contexts;
  - a provider error fails only the rules that use it;
  - an unregistered name fails the rule;
  - actions call `GetContextAsync` (shared cache, unused-by-conditions contexts load on demand);
  - no factory → `@` rules fail;
  - `changed` on a context fails the rule;
  - cancellation propagates.
- **`DirectContextProviderFactory`**: add, case-insensitive lookup, duplicates throw.
- **DI** (`AddRuleLoomTests`): `AddContextProvider<T>` resolves scoped providers; the engine gets the factory.
- **Integration:** a JSON rule on JSON state using `@customer.Tier`, plus an action reading a collection context.

## Docs

- JSON guide: "Contexts" section (`@` paths, providers, loading, failures) and the reserved `#`.
- Quick start: registering a context provider.
- `AGENTS.md`: contexts in Key concepts and Extension points.
