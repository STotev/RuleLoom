# Design: `$current` and `$previous` Scope Prefixes

**Issue:** [#38 API hardening: $current and $previous scope prefixes](https://github.com/STotev/RuleForge/issues/38) (also closes #16)
**Parent PRD:** [#22 API hardening before 1.0](https://github.com/STotev/RuleForge/issues/22) (module 5, user stories 49–52)
**Date:** 2026-10-06
**Status:** Approved (AFK)

---

## Problem

`current.` and `previous.` are reserved words in paths, so they collide with real property names: a property named
`Previous` was only reachable as `current.Previous.X`. And `changed` built `current.{path}` itself, so a scoped operand
such as `current.Price` became `current.current.Price` and `changed` was always false (#16).

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Syntax | `$current.` / `$previous.` select the state; unprefixed paths mean the current state | PRD #22 decision (2026-10-03): `$` is the usual variable/root marker (MongoDB, JSONPath) |
| Case | `$current` / `$previous` match ignoring case | Names ignore case (#37) |
| Variable alone | `$current` / `$previous` without a property return the whole state object | Natural meaning; costs nothing |
| `current.` / `previous.` | Ordinary property names when resolving | No ambiguity any more |
| Other `$` names | Reserved: `EvaluationContext.Resolve` throws `InvalidOperationException`; the parser rejects them | Keeps the namespace free for future `$now`, `$event` (PRD Out of Scope) |
| Migration guard | The parser rejects paths and schedule `referencePath` values whose first segment is `current` or `previous` (any case) followed by `.`, with a hint: `The 'current.' prefix is no longer supported; use '$current.Price' instead.` | Otherwise old rules would silently resolve against a property named `current` and get `null`. Transitional, for at least one release |
| Guard side effect | While the guard exists, a JSON rule reaches a property literally named `Current`/`Previous` as `$current.Previous.X` (not `Previous.X`). Rules built in code aren't affected | Acceptable for the transition; removing the guard later lifts it |
| Constants | `EvaluationContext.CurrentVariable` / `PreviousVariable` (`"$current"`, `"$previous"`) | Used by the engine, `changed` and the parser; available to custom operators |
| `changed` (#16) | Strips a leading `$current.`/`$previous.` from its operand, so `Price`, `$current.Price` and `$previous.Price` all compare current vs previous `Price`; unknown `$` names throw | `changed` always compares the two states; a scope on its operand only names the property |

## Breaking changes (`feat!:`)

- Paths written `current.X` / `previous.X` must become `$current.X` / `$previous.X`. JSON rules using the old form
  are rejected by the parser with that hint; rules built in code silently resolve `current.X` as a property named
  `current`.
- Paths starting with any other `$` name are rejected / throw.

## Test plan

- `EvaluationContextTests`: `$current`/`$previous` in any case, nested; plain paths; legacy prefixes as property
  names; a property named `Previous` via a plain path; variables alone; unknown `$` variables throw.
- `ChangedOperatorTests`: unscoped, `$current.`, `$previous.`, upper-case prefix; unknown variable throws.
- `RuleJsonValidationTests`: unknown `$` variables and legacy prefixes rejected in paths and schedule
  `referencePath`, with locations and the migration hint; valid forms (`$current`, `current` alone, `Currently.X`)
  accepted.
- Integration: a JSON rule comparing `$current.Price` with `$previous.Price`.
- Existing tests converted to the new syntax.
