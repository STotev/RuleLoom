# Design: Parse-Time Structural Validation

**Issue:** [#31 API hardening: parse-time structural validation](https://github.com/STotev/RuleLoom/issues/31)
**Parent PRD:** [#22 API hardening before 1.0](https://github.com/STotev/RuleLoom/issues/22) (module 6, structural part)
**Date:** 2026-10-04
**Status:** Approved (AFK)

---

## Problem

`RuleJsonParser` accepts rules that can never run correctly:

- A rule without `eventType` parses with `EventType = null` and silently never matches.
- A rule without `name` parses with `Name = null`.
- A root condition that is a path or a value (`"condition": { "path": "Price" }`) parses, then fails at execution
  time with `Root must be operator`.
- Empty operator names, empty paths and nodes with several kinds at once (`{"operator": "eq", "path": "X"}`) parse,
  and the extra kind is silently ignored.

Errors that are reported don't say where in the rule the problem is.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Where validation runs | On the JSON document, **before** deserialization: parse to a `JsonElement`, validate its shape, then deserialize from the element | Locations can be computed exactly; the polymorphic converter only sees one node subtree and can't know its path in the document |
| Exception type | New public `RuleParseException : JsonException` in `RuleLoom.Abstractions.Rule` | Existing `catch (JsonException)` code keeps working; callers that care can catch the specific type |
| Location format | JSONPath, as in `JsonException.Path`: `$.condition.operands[1]`; for `ParseMany`, `$[2].condition`. The location is also included in the message | Matches the standard `System.Text.Json` convention; #39 (`RuleValidator`) reuses the same format |
| Required rule properties | `name`, `eventType`: non-empty, non-whitespace strings. `condition`: an operator node | PRD user stories 38–39 |
| C# `required` | `RuleDefinition.Name` and `EventType` become `required` (`Condition` already is) | Code-built rules get the same guarantee at compile time. All rules in the repo already set them |
| Condition nodes | Must be JSON objects with **exactly one** of `operator`, `path`, `value` | Ambiguous nodes were silently resolved by priority |
| `operator` | Non-empty string. `operands`, when present, must be an array; each element is validated recursively | |
| Empty `operands` | Allowed | Operand counts are per operator (`MinOperands`/`MaxOperands`), so they are a semantic check for `RuleValidator` (#39), not a structural one. The March proposal's "at least one operand" rule is dropped for this reason |
| `path` | Non-empty string | |
| `value` | Any JSON value, including `null` | |
| Empty node `{}` | Rejected (previously read as a `null` value) | Ambiguous; `{"value": null}` says it explicitly |
| Actions | `actions`, when present, must be an array of objects, each with a non-empty `type`. An action `condition`, when present, must be an operator node. `parameters`, when present, must be an object | Same rules wherever a condition appears |
| Schedules | Not validated here | Enum values are already checked by deserialization. Missing `amount`/`unit` surfaces as a `Failed` action result. Candidate for #39 |
| Unknown properties | Ignored | Forward compatibility, as before |
| Validation of rules built in code | Not part of this slice | Covered by C# `required` for the required properties. #39 validates rules against registries |

## API

```csharp
namespace RuleLoom.Abstractions.Rule;

public sealed class RuleParseException : JsonException
{
    // JsonException.Path holds the JSONPath of the offending element.
    public RuleParseException(string message, string path);
}
```

`RuleJsonParser.Parse`, `ParseAsync` and `ParseMany` keep their signatures. Malformed JSON still throws a plain
`JsonException`; a well-formed document with an invalid rule throws `RuleParseException`.

## Examples

| JSON | Error (path) |
|---|---|
| `{"name":"R","condition":{...}}` | `eventType` is required (`$.eventType`) |
| `{"name":"","eventType":"E",...}` | `name` must be a non-empty string (`$.name`) |
| `"condition": { "path": "Price" }` | the root condition must be an operator (`$.condition`) |
| `{"operator":"and","operands":[{"operator":"eq","operands":[...]},{}]}` | node must have exactly one of `operator`, `path`, `value` (`$.condition.operands[1]`) |
| `[ok, ok, {"name":"R3","eventType":"E"}]` via `ParseMany` | `condition` is required (`$[2].condition`) |
| `"actions":[{"parameters":{}}]` | action `type` is required (`$.actions[0].type`) |

## Test plan

- One test per structural rule, asserting the exception type and `Path`.
- `ParseMany` reports the index of the failing rule; `ParseAsync` validates too.
- Malformed JSON still throws `JsonException`.
- Existing parser tests that used a path or value as the root condition are rewritten to wrap those nodes in an
  operator; the node-type inference they test is unchanged.

## Breaking changes

`feat!:` previously accepted rules are now rejected (missing or empty `name`/`eventType`, non-operator root
conditions, empty `{}` nodes, nodes with several kinds), and code that constructs `RuleDefinition` without `Name` or
`EventType` no longer compiles.
