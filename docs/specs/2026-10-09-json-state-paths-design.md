# Design: Resolve Paths on JSON State

**Issue:** [#68 Resolve paths on JSON state (JsonElement, JsonNode)](https://github.com/STotev/RuleForge/issues/68)
**Parent PRD:** [#66 JSON state and central-service spike (0.5)](https://github.com/STotev/RuleForge/issues/66) (user stories 1–12, 16, 19–24)
**Date:** 2026-10-09
**Status:** Approved (AFK)

---

## Problem

Paths resolve only through CLR properties, using one compiled accessor per (root type, path) built from **declared**
property types. Without a class, System.Text.Json produces a `JsonElement` (`JsonSerializer.Deserialize<object>`,
ASP.NET Core model binding) or a `JsonNode`. Both hold fields as data, not as C# properties, so every path silently
resolves to `null`. A central rules service can't evaluate rules on the events it receives.

The same declared-type limit affects CLR objects: a property declared as `object` or an interface (e.g. an envelope's
`object Payload`) can't be traversed, even when the runtime value has the property.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Supported JSON types | `JsonElement` and `JsonNode` (`JsonObject`, `JsonArray`, `JsonValue`) | What System.Text.Json produces without a class. JSON has no separate dictionary type. No legacy dynamic types (`ExpandoObject`, `dynamic`, `JToken`) or in-memory dictionaries (PRD #66 Out of Scope) |
| Where JSON can appear | Anywhere: as the root, under a property declared as JSON, or under a property declared as `object`/an interface/a base type whose runtime value is JSON | An envelope class with a JSON payload must resolve naturally, however the payload is declared |
| Crossing from CLR to JSON | **Runtime hand-off.** The cached entry for (type, path) holds the compiled accessor for the **longest prefix** that resolves on declared types, plus the number of segments it consumed. If segments remain, the runtime value at that point decides: `null` → `null`; `JsonElement`/`JsonNode` → the JSON walker resolves the rest; a CLR object of another type than declared → resolve the rest from that type (its own cached entry); the declared type itself → the property doesn't exist → `null` | CLR-only paths stay one compiled call, exactly as today (user story 22). One extra check where a compiled run ends handles JSON and polymorphic properties |
| Declared-type limit for CLR objects | Fixed by the same hand-off: a property declared as `object`/an interface/a base type is traversed using its runtime type | Same mechanism, no extra code; previously such paths always resolved to `null` |
| Inside JSON | An internal `JsonPathWalker` resolves the remaining segments; not cached (JSON is dynamic). Once inside JSON the walk stays in JSON | A JSON value can't contain a CLR object |
| Key matching | Scan the object's keys once: an exact-case match wins; otherwise a unique case-insensitive match; two or more candidates (including **duplicate exact keys**, which `JsonDocument` allows) are ambiguous → `null` | The same rules as properties (`OrdinalIgnoreCase`, exact case wins, ambiguous → `null`); duplicates don't silently pick one |
| Non-objects | A further segment on a string, number, boolean, `null`, undefined element or array → `null`. No array indexing | Arrays are leaves (PRD #66 Out of Scope) |
| `JsonElement` leaves | `String` → `string` (no date parsing); `True`/`False` → `bool`; `Null`/`Undefined` → `null`; `Number` → `long` if an integral literal in range, else `decimal`, else `double` (beyond `decimal`'s range), else `null` (beyond `double`'s range, e.g. `1e400`); `Object`/`Array` → the `JsonElement` itself | Plain CLR values work with every operator, `ValueComparer`, `GetParameter<T>` and `ReferencePath` unchanged. `decimal` keeps exact values (`19.99`). `1.0` and `1e2` are `decimal`; equality across `long`/`decimal` already works in `ValueComparer`, and #69 makes `changed` use it. ISO date strings are compared as instants by #69 |
| `JsonNode` leaves | `JsonObject`/`JsonArray` → the node itself; a `JsonValue` wrapping a `JsonElement` → mapped as above; a `JsonValue` created from a CLR value (`JsonValue.Create(19.99m)`, a `DateTime`) → that CLR value unchanged | Values that are already typed stay typed |
| Empty path | Returns the root unchanged (`$current`/`$previous` alone return the whole JSON state) | Unchanged behaviour |
| Errors | Data problems never throw (missing keys, wrong kinds, ambiguity → `null`), as today. **Exception:** a `JsonElement` from a disposed `JsonDocument` throws `ObjectDisposedException` | A disposed document is a programming error (the document must outlive `ExecuteAsync`); turning it into `null` would make rules silently not match |
| Public API | Unchanged: `PathResolver.Resolve(object? root, string path)` and `EvaluationContext.Resolve(path)` | Everything that resolves paths (operators, `ScheduleCalculator`, actions) gets JSON support for free |
| Dependencies | None added; `System.Text.Json` is part of the shared framework and already used by `RuleForge.Abstractions` | Abstractions stays dependency-free |

## Behaviour change

Paths through a property declared as `object`, an interface or a base type used to resolve to `null` whenever the
property wasn't on the declared type; they now resolve through the runtime value. Rules on such paths that never
matched can now match. No rule that matched before stops matching, so this isn't marked breaking.

## Test plan

**Unit** (`PathResolverTests`, `EvaluationContextTests`):
- `JsonElement` roots from `JsonDocument` and from `JsonSerializer.Deserialize<object>`; `JsonNode`/`JsonObject` roots.
- Nested paths; a property declared as `JsonElement`/`JsonNode`; a property declared as `object` holding JSON and
  holding a CLR object; an interface-typed property.
- Key rules: exact case wins, unique case-insensitive match, ambiguous keys, duplicate exact keys → `null`.
- Missing keys, JSON `null`, paths through strings, numbers, booleans and arrays → `null`.
- Leaves: `bool`, `string`, integral → `long`, `19.99` → `decimal` (exact), `1.0`/`1e2` → `decimal`, `1e300` → `double`, `1e400` → `null`,
  objects and arrays returned as JSON; `JsonValue` wrapping a CLR `decimal` and a `DateTime` returned unchanged.
- `$current`/`$previous` on JSON, alone and with paths.
- A disposed `JsonDocument` throws `ObjectDisposedException`.
- CLR paths still compile once per (type, path) (existing cache tests).

**Integration** (`RuleEngineIntegrationTests`): JSON rules run by `RuleEngine` against `JsonElement` and `JsonNode`
state: `eq`, `gt`/`lt` on numbers, `$previous` comparisons, an action condition, and a scheduled action whose
`ReferencePath` is an ISO 8601 string in the JSON.

## Docs

- JSON guide: a "Rules on JSON state" section (supported types, value mapping, key rules, limits, keeping the
  `JsonDocument` alive). #69 adds how ISO date strings compare.
- `AGENTS.md`: the paths note mentions JSON state and the runtime hand-off.
- `docs/roadmap.md`: the 0.5 slice table (#68–#71).
