# Design: The ASP.NET Core Sample on Typed Actions, `$path` and `MapRuleLoom`

**Issue:** [#119 Sample and guides: typed actions, `$path`, `MapRuleLoom`](https://github.com/STotev/RuleLoom/issues/119)
**Parent PRD:** [#113 Typed actions and authoring endpoints (0.7)](https://github.com/STotev/RuleLoom/issues/113) (user stories 82–83)
**Builds on:** [typed actions](2026-10-26-typed-actions-design.md), [paths in parameters](2026-10-26-path-parameters-design.md),
[parameter validation](2026-10-26-parameter-validation-design.md), [MapRuleLoom](2026-10-26-map-ruleloom-design.md)
**Date:** 2026-10-26
**Status:** Implemented

---

## Problem

The ASP.NET Core sample still shows 0.6: `send-email` reads `to` as a path string and resolves it itself, and the app
maps its own `GET /catalog`. 0.7 should be visible in it, and the guides should cover typed actions and `$path`.

## Decisions

Reviewed with the maintainer before building (HITL).

| Question | Decision |
|---|---|
| `send-email` | `IAction<SendEmailParameters>` with `SendEmailParameters(string To, string Subject)`; `SendEmailDescriptor : ActionDescriptor<SendEmailAction, SendEmailParameters>` labels both parameters. The action no longer resolves paths |
| Rules | `"to": { "$path": "Customer.Email" }` (and `Seller.Email`); the VIP order emails `{ "$path": "@customer.Email" }`, a context path: prefetched with the rule's contexts and checked against the customer's schema |
| Endpoints | `app.MapRuleLoom("/rules")` replaces the sample's `GET /catalog` (now `/rules/catalog`) and adds `/rules/validate`; no authorization, with a comment where `RequireAuthorization()` goes |
| A bad parameter | A rule in `promotions.json` with `"subjet"`: an unknown parameter and the missing `subject`, reported in the event response's invalid rules and by `/rules/validate` |
| `.http` | The catalog request moves; validate requests for a valid rule, a rule with parameter errors and a `$path` warning, and an array with a malformed rule |
| Scheduled actions | `RunScheduledActionJob` builds the `EvaluationContext` as the engine does (event type, catalog, context provider factory), so parameter paths into contexts resolve when the action runs; the event type goes into the job data |
| Guides | Schemas and contexts, JSON rules, quick start and the upgrading guide cover typed actions and `$path` |

## Findings

Recorded while building. None blocks 0.7.

1. **Running a scheduled action later is still the app's code.** `RunScheduledActionJob` repeats what the engine does
   for an immediate action: build the `EvaluationContext` (event type, catalog, context provider factory), find the
   action in the catalog, create it, run it, turn exceptions into a failed result. With `$path` parameters, getting the
   context wrong now shows only when the action runs: `new EvaluationContext(current, previous)` works for `Customer.Email`
   and fails `@customer.Email` as an undeclared context. A RuleLoom entry point (e.g. running a `ScheduledAction` with
   its event's state) would remove the duplication; a candidate for the backlog.
2. **`ScheduledAction` doesn't carry the event type**, which that context needs; the app stores it in the job data
   next to the state. An entry point as in 1 would take it with the state.
3. **A malformed rule has no name in the sample's report** (`InvalidRule.Rule` is `null`): `ParseEach` gives the index
   and the path, not the id, by design (#81). The location is enough to find it; no change.
4. **A typed action reads a path written as text as text.** Switching `send-email` to typed parameters and its rules to
   `$path` must happen together: a rule still saying `"to": "Customer.Email"` validates (it's text) and emails the
   literal string. The upgrading guide says so; the validator can't tell.

## Deviations

None.
