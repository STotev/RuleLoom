# Design: Validating Action Parameters

**Issue:** [#117 Validate action parameters against their schema](https://github.com/STotev/RuleLoom/issues/117)
**Parent PRD:** [#113 Typed actions and authoring endpoints (0.7)](https://github.com/STotev/RuleLoom/issues/113) (user stories 69–72)
**Builds on:** [typed actions](2026-10-26-typed-actions-design.md) (#115), [paths in parameters](2026-10-26-path-parameters-design.md) (#116)
**Date:** 2026-10-26
**Status:** Implemented

---

## Problem

A typed action's parameters are described in the catalog (`ActionRegistration.Parameters`), but `RuleValidator` doesn't
look at them: a misspelt parameter, a missing one or a number where text is expected is saved and fails only when the
action runs. Parameter paths (`{ "$path": ... }`) aren't checked against the event and its contexts either.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Which actions | Actions whose registration has a parameters schema (typed actions). Untyped actions, and actions not in the catalog (already an error), aren't checked | Validation never invents rules |
| Unknown parameter | Error at `$.actions[i].parameters.<name>`: the schema has no field of that name (matched like paths: exact case wins, else one match ignoring case) | Binding ignores unknown names, so a typo silently leaves a parameter unset |
| Missing required | Error at `$.actions[i].parameters` naming the parameter | Binding fails on it at run time |
| Required in the schema | `FieldDefinition.IsRequired`: the field is listed in its object's `required`. Read for every schema (events and contexts too), so nested objects are checked the same way | One description of the data; a UI marks required fields from it. A new positional parameter of a public record: breaking, noted in the upgrading guide |
| Values | A value is checked as the JSON it is bound from (serialized with `ParametersSchemaDefinition.JsonOptions`, as binding does), against its field: text, whole numbers, numbers, booleans, dates and dates with times (ISO 8601 text), enum names (ignoring case), objects (unknown fields, missing required fields and their values, recursively; dictionaries aren't looked into), lists (each item), any value. `null` only where the field is nullable. A mismatch is an error at the value's location | The wrong JSON type fails binding; the same rules for JSON rules and rules built in code |
| Enum numbers | An error, although binding accepts them | The schema lists names; rules use names |
| Path parameters | Of every action, typed or not (they're the rule's paths). Visited by the walker like other paths, at `$.actions[i].parameters.<name>.$path`: syntax, unknown fields and contexts, undeclared contexts, paths into lists are errors; contexts without a schema are warnings | "Checked like condition paths" |
| Path type | A warning when the path's field can't be bound as the parameter: numbers ↔ numbers; text ← text, dates, enums; dates ← dates, text; enums ← enums, text, whole numbers; booleans ← booleans; objects and lists ← the same; any value on either side passes. Nullability isn't compared | JSON state may still convert; a warning doesn't block the rule |
| Where | A `ParameterCheck` in the existing walk; a new `RuleCheck.Parameters` hook runs after the action's parameter paths were visited, like `Operands` | One walk, small checks |

## Deviations

None.
