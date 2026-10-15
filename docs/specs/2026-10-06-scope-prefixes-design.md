# Design: `$current` and `$previous` Scope Prefixes

**Issue:** [#38 API hardening: $current and $previous scope prefixes](https://github.com/STotev/RuleLoom/issues/38) (also closes #16)
**Parent PRD:** [#22 API hardening before 1.0](https://github.com/STotev/RuleLoom/issues/22) (module 5, user stories 49–52)
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
| Other `$` names | Reserved: `EvaluationContext.Resolve` throws `InvalidOperationException`; the parser rejects them with their location | Keeps the namespace free for future `$now`, `$event` (PRD Out of Scope) |
| Migration guard for the old prefixes | **None** (maintainer decision, 2026-10-06). `current.X` / `previous.X` are ordinary property paths in JSON and code alike, so a property named `Previous` is reached as `Previous.X` | The package has never been published, so no existing rules need migrating. PRD #22 user story 51 and the transitional guard first proposed here are dropped |
| How operators pick a state | Through the path string: `ctx.Resolve("$previous.Price")` or a `$previous.Price` operand via `GetValue`. No public constants or scope types | `Resolve` already understands the prefixes; a dedicated API waits for the variables/functions design (roadmap backlog) |
| `changed` (#16) | Strips a leading `$current.`/`$previous.` from its operand and resolves `$current.{property}` and `$previous.{property}`, so `Price`, `$current.Price` and `$previous.Price` are equivalent | `changed` always compares the two states; a scope on its operand only names the property |
| Unknown `$` name in a `changed` operand | Not detected by `changed` itself (it resolves to `null` on both sides, i.e. not changed). JSON rules are protected because the parser rejects unknown `$` names; every other operator throws via `Resolve` | Keeps `changed` to a plain prefix check rather than its own variable parsing |

## Breaking changes (`feat!:`)

- Paths written `current.X` / `previous.X` must become `$current.X` / `$previous.X`; the old form now resolves
  `current.X` as a property named `current` (no migration guard, since the package is unpublished).
- Paths starting with any other `$` name are rejected / throw.

## Test plan

- `EvaluationContextTests`: `$current`/`$previous` in any case, nested; plain paths; legacy prefixes as property
  names; a property named `Previous` via a plain path; variables alone; unknown `$` variables throw.
- `ChangedOperatorTests`: unscoped, `$current.`, `$previous.`, upper-case prefix.
- `RuleJsonValidationTests`: unknown `$` variables rejected in paths and schedule `referencePath`, with locations;
  valid forms (`$current`, `current`, `current.Price`, `Previous.Order.Status`, `Currently.X`) accepted.
- Integration: a JSON rule comparing `$current.Price` with `$previous.Price`.
- Existing tests converted to the new syntax.
