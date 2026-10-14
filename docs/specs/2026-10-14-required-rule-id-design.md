# Design: Require a Rule ID

**Issue:** [#77 Require a rule id](https://github.com/STotev/RuleForge/issues/77)
**Parent PRD:** [#66 JSON state and central-service spike (0.5)](https://github.com/STotev/RuleForge/issues/66) (spike follow-up)
**Date:** 2026-10-14
**Status:** Approved (AFK)

---

## Problem

`RuleDefinition.Id` defaults to a new GUID, so a rule without an `id` gets a different identity every time it's parsed
or constructed. The rules-service spike (#70) re-reads rules for every event, so such a rule can't be used to key
scheduled jobs, and results can't be traced back to the stored rule. A rule's identity belongs to where it's stored (a
file, a database key), so the author or the store must supply it.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| `RuleDefinition.Id` | `required string Id`, no default | Like `Name`, `EventType` and `Condition`; the compiler enforces it for rules in code |
| JSON | `id` is required and must be a non-empty string, checked with the other required properties; errors are a `RuleParseException` at `$.id` (`$[n].id` in `ParseMany`) | Same check and message as `name`/`eventType` (`'id' is required.`, `'id' must be a non-empty string.`) |
| Numeric IDs (`"id": 42`) | Rejected (must be a string) | `Id` is a string; a store with numeric keys writes them as strings. Keeps one representation |
| Uniqueness | Not checked | Rules come from the consumer's store, which owns uniqueness; the engine doesn't depend on it |

## Breaking changes (`feat!:`)

- Rules in JSON without an `id` fail to parse (`RuleParseException` at `$.id`).
- `new RuleDefinition { ... }` without `Id` doesn't compile.

Migration: give each rule a stable `id`, for example its key in your rule store.

## Test plan

- `RuleJsonValidationTests`: missing, `null`, empty, whitespace and numeric `id` → `RuleParseException` with the
  path, for `Parse` and `ParseMany` (`$[1].id`).
- `RuleJsonParserTests`: `id` is read (case-insensitive property name, as for other properties).
- Existing tests and samples set ids.

## Docs

- Upgrading guide (0.4 → 0.5), JSON guide (rule properties, examples), quick start, `AGENTS.md` (required properties).
