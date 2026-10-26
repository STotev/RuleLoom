# Design: Context Declarations Always Name Their Provider

**Issue:** [#114 Context declarations always name their provider](https://github.com/STotev/RuleLoom/issues/114)
**Parent PRD:** [#113 Typed actions and authoring endpoints (0.7)](https://github.com/STotev/RuleLoom/issues/113) (user story 74)
**Found in:** [schemas and contexts sample, finding 1](2026-10-25-schemas-contexts-sample-design.md#findings)
**Date:** 2026-10-26
**Status:** Implemented

---

## Problem

An event schema's context declaration carries `x-ruleloom-provider` only when the schema sets `LoadedBy`. Otherwise a
UI has to know the default (the provider named after the context) to find the context's schema in the catalog document.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Writing | The generator always writes `x-ruleloom-provider`: the `LoadedBy` provider, or the context's name | Every declaration names its provider |
| Reading | A declaration without it (or with `null`) still means the context's name, as before; an empty or non-string provider is still an error | Documents written by 0.6 keep working |
| Older documents | Reading fills the default into the document, so a stored schema written without it is written with it (`ToJson`, the catalog document, the next store save) | The catalog document names every provider, wherever its schemas came from |

## Deviations

None.
