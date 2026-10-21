# Design: Schema-Aware Rule Validation

**Issue:** [#94 Schema-aware rule validation with severities](https://github.com/STotev/RuleLoom/issues/94)
**Parent PRD:** [#87 Schemas and contexts (0.6)](https://github.com/STotev/RuleLoom/issues/87) (user stories 23–32)
**Date:** 2026-10-21
**Status:** Approved

---

## Problem

`RuleValidator` checks operators, operand counts, actions and schedules, but not paths: a typo (`Totl`), an ignored
field, a context the event doesn't relate to or a context without a provider is only discovered when the rule runs.
At that point it quietly resolves to `null` or fails the rule. With event schemas (#92), the schema store (#93) and
context schemas (#101), the validator can check paths before a rule is saved.

## When validation runs

The library never validates on its own; consumers validate where rules are loaded or saved. For example, the ASP.NET
Core sample's `TenantRuleStore` validates each rule as it reads the tenant's rule file, right before the engine runs
it. That is the same scope the engine runs in, with the same Scoped (possibly tenant-aware) `ISchemaStore`. So the
validator reads schemas from the store, as the engine does, rather than taking them from the caller: validation and
execution always see the same schemas, and tenancy is the store's concern (PRD story 30 is met by the scoped store).

## API

```csharp
public sealed class RuleValidator(
    IEnumerable<IConditionOperator> operators,
    IActionFactory actionFactory,
    IContextProviderFactory? contextProviders = null,   // new: registered context names
    ISchemaStore? schemas = null)                       // new: event and context schemas
{
    public RuleValidationResult Validate(RuleDefinition rule);                                // unchanged
    public Task<RuleValidationResult> ValidateAsync(RuleDefinition rule, CancellationToken cancellationToken = default);
}

public sealed record RuleValidationResult(IReadOnlyList<RuleValidationError> Errors)
{
    public IReadOnlyList<RuleValidationError> Warnings { get; init; } = [];   // new
    public bool IsValid => Errors.Count == 0;                                 // warnings don't make a rule invalid
}
```

`ValidateAsync` runs every check of `Validate`. When there is a store and it has a schema for the rule's event type,
it also checks paths. Without a store, or without a schema for the event, it returns what `Validate` returns: schemas
stay optional (story 29). DI's `RuleValidator` registration passes the context provider factory and the store.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Where schemas come from | The injected `ISchemaStore`: `GetEventAsync(rule.EventType)` once, and `GetContextAsync(provider)` once per context the rule uses | Same source and scope as the engine (see *When validation runs*) |
| Sync method | `Validate(rule)` stays and never reads schemas | Non-breaking; the store is async |
| Severity | A separate `Warnings` list on the result, of the same record type; `IsValid` still means no errors | Separates "broken, block the save" from "suspicious, show it" without a breaking change; a severity enum would only add speculative generality |
| Which paths | Every `PathOperand` in rule and action conditions, and schedule `referencePath`s; locations are the JSONPath of the path (`$.condition.operands[0].path`, `$.actions[1].schedule.referencePath`) | The same places `ContextReferences` collects contexts from |
| Matching | Path segments match field names (JSON names) ignoring case; an exact-case match wins and an ambiguous case-insensitive match is unknown | Same rules as `PathResolver` |
| Subject paths | `Total`, `$current.Total` and `$previous.Total` are checked against the event's fields (the previous state has the subject's shape, story 31). `$current`/`$previous` alone (the whole subject) is valid | — |
| Contexts | `@name` must be declared by the event schema (ignoring case), and its provider (`ContextDefinition.Provider`) must be registered in the provider factory; the rest of the path is checked against the provider's context schema. `@name` alone is valid | An undeclared or unloadable context never resolves (story 25) |
| Where paths can't be checked | Below `Any` fields (untyped: `object`, JSON), objects without fields (dictionaries, repeated types) and contexts without a schema (untyped providers), paths aren't checked. A context without a schema gives one **warning** per path ("paths into it aren't checked") | The data has no declared shape; untyped providers stay usable |

### Errors

- An unknown field (including ignored fields, which aren't in the schema): `'Totl' is not a field of OrderPlaced`.
- A path below a value that has no fields: below a scalar (`Total.Amount`), or into a list (`Lines.Sku`; paths can't
  index lists, so they resolve to `null`), including a collection context (`@watchers.Email`).
- An undeclared context: `'@account' is not a context of OrderPlaced`.
- A declared context whose provider isn't registered: `No context provider is registered under 'account' (context '@toAccount')`.
- A context path after a state (`$current.@customer`, `$previous.@customer`): contexts have no state. The parser
  already rejects this; the validator also catches it in rules built in code.
- `changed` on a context path: the engine throws for it, because contexts have no previous state.

### Warnings: types that can't match

Only for the built-in comparison operators (`eq`, `gt`, `gte`, `lt`, `lte`), recognised by their type rather than
their name, so a custom operator registered as `eq` isn't judged by built-in rules. For each pair of a typed field
path and a value operand (or two typed field paths), following `ValueComparer`:

| Field type | Compatible values |
|---|---|
| `Integer`, `Number` | numbers, CLR enums |
| `String` | strings, `DateTime`/`DateTimeOffset` (compare with ISO 8601 text), CLR enums (match by name) |
| `Boolean` | `true` / `false` |
| `DateTime`, `Date` | `DateTime`/`DateTimeOffset` values and strict ISO 8601 strings (not `DateOnly`) |
| `Enum` | not checked: CLR enums match by C# name or number, JSON state by its serialized text |
| `Object`, `Array`, `Any` | not checked for `eq` (JSON values compare by content) |

- `null` is compatible with anything; a non-nullable field compared with `null` isn't flagged (the payload may omit it).
- Ordering operators (`gt`, `gte`, `lt`, `lte`) on an `Object` or `Array` field: a warning, because those values can't
  be ordered (booleans can: `false < true`).
- At most one warning per comparison.
- Two field paths of incompatible types (e.g. a number and a boolean): a warning.

These are warnings, not errors, because the rule still runs (it just never matches), and the payload may not follow
the schema exactly (untyped JSON).

## Test plan

`RuleValidatorTests`-style, with schemas built from `EventSchema<T>` / `ContextDescription<T>` and kept in an
`InMemorySchemaStore`:

- **Unchanged:** without a store, or without a schema for the event, `ValidateAsync` returns what `Validate` returns.
- **Subject paths:** known nested fields pass, case-insensitive; unknown and ignored fields fail with their location;
  `$previous.` paths are checked; paths below scalars and into lists fail; paths below `Any`, dictionaries and repeated
  types pass; schedule reference paths and action conditions are checked.
- **Contexts:** declared with a provider and schema → checked; undeclared → error; no provider → error; no schema →
  warning; collection context: whole passes, sub-path fails; `LoadedBy` uses the provider's schema; `$previous.@x`
  (built in code) and `changed` on a context → errors.
- **Types:** each row of the table, ordering on booleans, two paths of different types, `null` values, a custom
  operator named `eq` not checked.
- **Result:** warnings don't make a rule invalid; all problems are reported at once.
- **DI:** the resolved `RuleValidator` uses the registered providers and store.

## Docs

- JSON guide (validation): `ValidateAsync`, what's checked with a schema, warnings.
- Quick start: validating against schemas.
- `AGENTS.md`: validation (property paths are now validated with a schema).
- The ASP.NET Core sample moves to `ValidateAsync` in #95.

## Deviations

- Rules built in code skip the parser, so with a schema the validator also reports what the parser would reject in
  paths: unknown `$` variables (`$state.Total`) and `#` paths (reserved for functions).
- The new constructor parameters are optional, so existing code compiles unchanged (callers compiled against 0.5
  need a rebuild).
- After code review: the checks that need no schema (`#` paths, a bare `@`, unknown `$` variables, contexts after a
  state, `changed` on a context) moved into `Validate`, so they also apply without schemas. `ValidateAsync` adds only
  what needs a schema.
- After code review: booleans can be ordered (`ValueComparer` orders same-typed comparable values), so only objects and
  lists get the ordering warning; enum fields aren't judged (CLR state matches enum names and numbers, JSON state the
  serialized text, and the schema can't tell which); value compatibility follows `ValueComparer` for values built in
  code (`DateTime` against text, CLR enums, `DateOnly`, `Guid`).
- Known limit: an enum serialized as a number is an `Integer` field in the schema, so comparing it with a name
  (`eq(Status, "Shipped")`) is a warning. That's right for JSON state (the payload has the number) but not for CLR
  state, where names match.
