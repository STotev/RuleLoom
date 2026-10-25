# Design: Sample and Guide for Schemas and Contexts

**Issue:** [#95 Sample and guide for schemas and contexts](https://github.com/STotev/RuleLoom/issues/95)
**Parent PRD:** [#87 Schemas and contexts (0.6)](https://github.com/STotev/RuleLoom/issues/87) (user stories 54–55)
**Date:** 2026-10-25
**Status:** Implemented

---

## Problem

0.6 added event schemas, contexts, validation against schemas and the [catalog](2026-10-25-catalog-design.md). The
ASP.NET Core sample uses the catalog only for its event schemas and actions, and there's no guide to schemas and
contexts. A reader can't see a context used in a rule, a context read by an action, or what a rule-authoring UI would
read. This slice shows them in one application and records what was awkward to build.

Service-to-service features (exporting schemas to another app, publishing) are out of scope (monolith first; see
`GLOSSARY.md` and `docs/adr/`).

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Catalog document | `GET /catalog` returns `catalog.ToJson()` | What a rule-authoring UI reads: events and their contexts, fields with labels, operators, actions |
| Single context | `OrderPlaced`/`OrderUpdated` declare `Context("customer").ReferencedBy(o => o.Customer.Email)`; `CustomerProvider : ContextProvider<CustomerProfile>` reads an in-memory `CustomerDirectory`; `CustomerProfileSchema : ContextSchema<CustomerProfile>` adds labels | The order DTOs don't change; the reference is a nested path; the schema shows labels reaching the catalog document |
| Rule on a context | "VIP order" becomes `Total gte 500` **and** `@customer.Tier eq "gold"` | A context path in a condition, prefetched by the engine |
| Collection context | `OrderPlaced` declares `orderHistory` (same reference); `OrderHistoryProvider : CollectionContextProvider<PastOrder>`; a new `loyalty-check` action reads it with `GetContextAsync<IReadOnlyList<PastOrder>>` and reports the count and total | Rules can't use paths into lists, so an action is the natural reader |
| Descriptions | `ActionDescriptor<T>` for `send-email` and `loyalty-check`, found next to the actions by `AddFromAssemblyOf` | Shows descriptors reaching the catalog document |
| Invalid path | A promotions rule using `@customer.Teir` is reported next to the existing invalid `send-sms` rule | Validation against a context's schema, with its location |
| Store | Not used | In one application an in-memory store adds nothing visible; the guide says when to use one |
| Guide | `docs/guides/schemas-and-contexts.md`, linked from the README and the docs index, walking through the sample | User story 55 |
| Verification | `.http` requests, run manually; no test project | As for #70 |

## Findings

Recorded while building. None blocks 0.6; the first is for the 0.7 authoring surface.

1. **The link from a context to its schema is implicit in the catalog document.** An event's context declaration carries
   `x-ruleloom-provider` only when the schema sets `LoadedBy`; otherwise a UI has to know that the provider (and so the
   context schema) is the one named after the context. Consider writing the provider on every declaration when the
   authoring surface is designed (0.7).
2. **Providers null-forgive the reference** (`request.GetReference<string>()!`) even though the event declares
   `ReferencedBy`: the reference is `null` only when the event's field is. Acceptable; the guide shows the pattern.
3. **Collection contexts come back nullable** from `GetContextAsync<T>`, so the action writes `?? []` although a
   `CollectionContextProvider` never returns `null`. Minor; no change.
4. **Context names are written twice**, in the schema (`Context("customer")`) and at registration
   (`AddContextProvider<CustomerProvider>("customer")`). A mismatch stops the app at startup with a clear message, which
   is enough.

## Deviations

None.
