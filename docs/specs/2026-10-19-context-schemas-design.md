# Design: Context Schemas

**Issue:** [#101 Context schemas described with their providers](https://github.com/STotev/RuleLoom/issues/101)
**Parent PRD:** [#87 Schemas and contexts (0.6)](https://github.com/STotev/RuleLoom/issues/87) (user stories 11–13)
**Date:** 2026-10-19
**Status:** Approved (AFK)

---

## Problem

Event schemas declare which contexts an event relates to (`Context("toAccount").LoadedBy("account").ReferencedBy(...)`),
but nothing describes a context's fields. The validator (#94) can't check `@toAccount.balance`, and a UI can't offer
"To account › Balance". A context is described once, by the kind of entity a provider loads (`account`), wherever its
data comes from; every event that relates to an account shares that description.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| How contexts are described | `ContextSchema<T>` classes, like `EventSchema<T>`: `Context("account")` (required; the provider name it describes) with `Label`/`Description`/`Many()`, and `Field(a => a.Balance)` with `Label`/`Description`/`Ignore()` | Same style and machinery as event schemas; one file per kind of entity |
| Collections | `Context("watchers").Many()` on a `ContextSchema<WatcherDto>`: the provider returns a collection of `WatcherDto`; fields describe one element | Matches `ContextProvider<IReadOnlyList<WatcherDto>>` |
| Default shape for typed providers | **None**: only contexts with a `ContextSchema` have a shape | Explicit, as with event names; no inference from provider types |
| Model and JSON | `ContextSchemaDefinition` (`Name`, `Label`, `Description`, `IsMany`, `Fields`; `ToJson()`, `Parse(json)`), written as a JSON Schema 2020-12 document with `x-ruleloom-context` (the type is `object`, or `array` with `items` for `Many()`) | Same generator, normalisation and reader as event schemas |
| Collections of schemas | `EventSchemaCollection` becomes **`SchemaCollection`**: `Add<T>()`, `Add(schema)`, `AddFromAssembly…` take event and context schema classes (`IEventSchema`, `IContextSchema`); `Build()` returns a **`SchemaSet`** (`Events`, `Contexts`) with `ToJson()` (one array of both document kinds) and `SchemaSet.Parse(json)`. Duplicate event types or context names throw. `EventSchemaDefinition.ParseMany` is replaced by `SchemaSet.Parse` | Exports, imports and the store carry both kinds together, so a catalogue or UI gets everything from one place |
| Store | `ISchemaStore` covers both kinds, with explicit names: `GetEventAsync`/`ListEventsAsync`/`SaveEventAsync` and `GetContextAsync`/`ListContextsAsync`/`SaveContextAsync` (context names ignore case); `InMemorySchemaStore` implements both | One store for RuleLoom's schema data; names say which kind |
| Registration | `AddSchemas` sources pick up context schema classes (assembly scan, `Add<T>()`) and JSON containing context documents; both kinds are saved into the store (in-memory start, or at startup for your own store) | Context schemas are sources like event schemas |

The Schemas package is unreleased, so the renames aren't breaking changes.

## Example

```csharp
public sealed class AccountSchema : ContextSchema<AccountDto>
{
    public AccountSchema()
    {
        Context("account").Label("Account");
        Field(a => a.Balance).Label("Balance");
        Field(a => a.InternalRiskScore).Ignore();
    }
}

services.AddRuleLoom(r => r
    .AddSchemas(s => s.AddFromAssemblyOf<AccountSchema>())    // event and context schemas
    .AddContextProvider<AccountProvider>("account"));
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

- **Context schema builder:** name required, labels, `Ignore`, `Many()` (array document, element fields), invalid configuration
  (as for event schemas), JSON round trip, malformed documents.
- **`SchemaCollection` / `SchemaSet`:** both kinds from an assembly, duplicates (event types, context names), mixed JSON round
  trip, `Parse` rejects unknown document kinds.
- **Store:** context get (ignoring case), list, save replaces; events unchanged under the new names.
- **DI:** context schemas from classes and JSON reach the in-memory store and, at startup, your own store.

## Docs

- JSON guide (contexts): describing a context's fields with `ContextSchema<T>`.
- Quick start ("Event schemas"): context schema example.
- `AGENTS.md`.
