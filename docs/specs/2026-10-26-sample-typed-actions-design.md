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

Recorded while building.

## Deviations

None.
