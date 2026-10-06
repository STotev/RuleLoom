# Design: Normalised Action Parameters and `GetParameter<T>`

**Issue:** [#35 API hardening: normalised action parameters and GetParameter<T>](https://github.com/STotev/RuleForge/issues/35)
**Parent PRD:** [#22 API hardening before 1.0](https://github.com/STotev/RuleForge/issues/22) (module 9 and the parameter part of module 6)
**Date:** 2026-10-06
**Status:** Approved (AFK)

---

## Problem

`ActionDefinition.Parameters` is a `Dictionary<string, object?>`. Rules built in code put plain CLR values in it, but
`System.Text.Json` deserializes `object` values as `JsonElement`, so every action author has to handle both shapes
(`action.Parameters["to"]?.ToString()` happens to work for both; `(decimal)action.Parameters["amount"]` works for
neither). There is no typed way to read a parameter.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Parameter values from JSON | Plain CLR values, using exactly the rules for condition values: `null`, `bool`, `int`/`long`/`double`, `string`, `List<object?>`, `Dictionary<string, object?>` (nested) | One shape regardless of where a rule comes from |
| How | An internal `ClrValueJsonConverter` (`JsonConverter<object>`) registered in the parser's options; condition values and parameters share one `JsonClrValues.ToClrValue` helper | Applies to every `object`-typed value the parser reads; today that's only parameters, since condition nodes have their own converter |
| Typed access | `ActionDefinition.GetParameter<T>(name)` and `TryGetParameter<T>(name, out T)` | PRD user story 36 |
| Conversion rules | `ValueConverter` (#30), unchanged: lossless numeric conversions (int → decimal, 0.5 → decimal), enum names/numbers, ISO 8601 strings → dates, `null` → reference/nullable types, assignable values as-is (e.g. `List<object?>` → `IReadOnlyList<object?>`) | One set of rules everywhere. Deliberately **no** number ↔ string or `"100"` → `int` conversion; an action can read `object` and convert itself if it needs that |
| Missing parameter | `GetParameter` throws `KeyNotFoundException`: `Action 'send-email' has no parameter 'cc'.` | Standard exception for a missing key, with context |
| Unconvertible value | `GetParameter` throws `InvalidOperationException`: `Parameter 'to' of action 'send-email' is String, which can't be converted to Int32.` | PRD user story 37 |
| `TryGetParameter` | `false` for missing or unconvertible values | |
| Parameter names | Exact match first, then a unique case-insensitive match; names that differ only in case and match neither exactly count as missing | Same rule as property paths (#37). The dictionary itself stays exact-case, since STJ creates it |
| `LogAction` | Unchanged (it prints whatever `message` holds) | Out of scope |

## Breaking change

Parameters parsed from JSON are no longer `JsonElement`. Code that called `JsonElement` members (`GetString()`,
`GetInt32()`, `ValueKind`) must read plain values instead, preferably via `GetParameter<T>`.

## Test plan

- Parser: every JSON value kind arrives as the documented CLR type; no `JsonElement` anywhere.
- `ActionDefinitionTests`: same-type reads, conversions (int → decimal/long/nullable, double → decimal, ISO string →
  `DateTimeOffset`, enum name), list values, null values, name case handling (exact wins, ambiguous is missing),
  missing and unconvertible errors with exact messages, `TryGetParameter` true/false.
- Integration: a JSON rule's parameters reach a custom action, which reads them with `GetParameter<T>`.
