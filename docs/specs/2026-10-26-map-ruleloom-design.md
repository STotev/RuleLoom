# Design: `MapRuleLoom` Authoring Endpoints

**Issue:** [#118 RuleLoom.AspNetCore: MapRuleLoom endpoints](https://github.com/STotev/RuleLoom/issues/118)
**Parent PRD:** [#113 Typed actions and authoring endpoints (0.7)](https://github.com/STotev/RuleLoom/issues/113) (user stories 77–81)
**Builds on:** [parsing rule by rule](2026-10-26-parse-rule-by-rule-design.md) (#81)
**Date:** 2026-10-26
**Status:** Implemented

---

## Problem

Every app with a rule-authoring UI writes the same two endpoints: one serving the catalog document, one validating a
rule. A new package maps them, leaving storage, tenancy and authorization to the app.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Package | `RuleLoom.AspNetCore` (`src/RuleLoom.AspNetCore`), a framework reference to `Microsoft.AspNetCore.App`, referencing `RuleLoom.Extensions.DependencyInjection`; packed like the others | The endpoints resolve the catalog and validator that `AddRuleLoom` registers |
| Entry point | `endpoints.MapRuleLoom(string prefix)` on `IEndpointRouteBuilder`, in `Microsoft.AspNetCore.Builder` (as `AddRuleLoom` sits in `Microsoft.Extensions.DependencyInjection`); returns the `RouteGroupBuilder` | The app adds `RequireAuthorization`, CORS or rate limits to the group |
| `GET {prefix}/catalog` | The catalog document (`catalog.ToJson()`), `application/json` | What a UI reads |
| `POST {prefix}/validate` body | One rule (a JSON object) or an array of rules | One rule while editing, a file on import |
| Arrays | Parsed rule by rule (`ParseEach`); each parsed rule is validated with `RuleValidator` | A malformed rule doesn't hide the others' problems |
| Response | `200` with a `RuleValidationResponse` for an object, or an array of them, in order, for an array: `index` (arrays only), `id` (parsed rules), `parsed`, `isValid`, `errors` and `warnings` (`{ path, message }`) | Parse errors, validation errors and warnings per rule; a malformed rule has one error and wasn't validated (`parsed: false`) |
| Paths | Relative to the rule (`$.condition`), in parse and validation errors alike; the parser's `$[2].condition` becomes `$.condition` in result 2 | The UI shows them next to the rule's fields |
| Messages | The parse error's message without the parser's `(at path)` suffix | The path is its own field |
| Malformed JSON, or neither object nor array | `400` problem details naming the problem | Nothing to report per rule |
| Storage | None | Storage and tenancy stay the app's |
| Tests | `tests/RuleLoom.AspNetCore.Tests` on `Microsoft.AspNetCore.TestHost` | Endpoints through HTTP |
| Sample | Unchanged here; #119 moves the ASP.NET Core sample to `MapRuleLoom` | #119 is the sample slice |

## Deviations

None.
