# Design: Paths in Action Parameters

**Issue:** [#116 Paths in action parameters (`$path`)](https://github.com/STotev/RuleLoom/issues/116)
**Parent PRD:** [#113 Typed actions and authoring endpoints (0.7)](https://github.com/STotev/RuleLoom/issues/113) (user stories 64–68)
**Decision:** [ADR 0003](../adr/0003-paths-in-parameters-are-marked.md)
**Date:** 2026-10-26
**Status:** Implemented

---

## Problem

An action that needs event or context data takes a path as text (`"to": "Customer.Email"`) and resolves it itself. The
validator can't check it, the engine can't prefetch the contexts it uses, and an authoring UI can't tell it from text.
This slice lets a rule mark a parameter as a path, `"to": { "$path": "@customer.Email" }`, and has RuleLoom resolve it.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Marker | A parameter whose value is an object with the single key `$path` (ignoring case, like rule property names) holding a valid path | ADR 0003 |
| Other keys | `$path` with other keys → `RuleParseException` at the object's JSONPath | The ambiguous case fails loudly |
| Nested | An object with a `$path` key anywhere inside a parameter's value → `RuleParseException` ("a path must be a parameter's whole value") | Lists of paths and paths inside values are out of scope; rejecting them keeps the meaning free to define later instead of silently passing a literal |
| Invalid path | The path's syntax error (`RulePath`) → `RuleParseException` at `…parameters.to.$path` | Same check as condition paths |
| Stored as | The parsed `RulePath` in `ActionDefinition.Parameters`; rules built in code put a `RulePath` there too | Parse once (`RulePath` is the only place that knows the syntax) |
| Reading | `GetParametersAsync<T>(context, ct)` resolves every path, then binds; `GetParameterAsync<T>(name, context, ct)` reads one parameter, resolving a path, converted like `GetParameter<T>` | Async: a context a path uses is loaded if it isn't yet (`GetContextAsync`), so scheduled actions run outside the engine resolve contexts as they are then |
| Resolution failure | `InvalidOperationException` naming the parameter and the path (undeclared context, failed load, no catalog) | Fails only that action |
| Without a context | `GetParameter<T>(name)` on a path throws, pointing at `GetParameterAsync`; `TryGetParameter<T>` returns `false` | A path can't be read without the event |
| Prefetch | `ContextReferences` adds the contexts of parameter paths | The engine loads them with the rest, once per event, in parallel |
| Operands, `schedule.referencePath` | Unchanged | ADR 0003 |

## Deviations

- **Reading parameters is async.** The PRD named `GetParameters<T>(context)` and `GetParameter<T>(name, context)`. A
  scheduled action runs outside the engine, where its contexts aren't loaded, and loading is async, so they are
  `GetParametersAsync<T>(context, cancellationToken)` and `GetParameterAsync<T>(name, context, cancellationToken)`.
  `GetParameters<T>(context)` from #115 was never released, so it's replaced rather than kept.
