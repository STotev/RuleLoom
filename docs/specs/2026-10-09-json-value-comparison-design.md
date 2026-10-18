# Design: Compare JSON Dates and Values by Meaning

**Issue:** [#69 Compare ISO date strings as instants; changed via ValueComparer](https://github.com/STotev/RuleLoom/issues/69)
**Parent PRD:** [#66 JSON state and ASP.NET Core spike (0.5)](https://github.com/STotev/RuleLoom/issues/66) (user stories 13–18)
**Date:** 2026-10-09
**Status:** Approved (AFK)

---

## Problem

Since #68, rules run on JSON state, but JSON has no date type: dates arrive as ISO 8601 strings. `ValueComparer`
only parses an ISO string when the *other* side is a `DateTime`/`DateTimeOffset`, so a JSON date compared with a date
in a rule is a string-vs-string comparison, which is **ordinal**. `"2026-05-01T14:00:00+02:00"` doesn't equal
`"2026-05-01T12:00:00Z"`, and `gt`/`lt` are only right when both strings happen to use the same offset and format.

`changed` compares with `object.Equals`, so the same value written differently counts as a change: `100` (`long`) vs
`100.0` (`decimal`), the same instant with different offsets, and any JSON object or array path (two `JsonElement`s
from different documents are never equal, even with identical content).

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Two ISO strings | When **both** strings are strict ISO 8601 (the formats `ValueConverter` already accepts), they compare as UTC instants for equality and ordering | JSON dates compare like `DateTime` properties do. Existing semantics apply unchanged: no offset = UTC, date-only = UTC midnight (the time semantics review in 0.8 may revisit them) |
| Exactly one ISO string | **Not comparable**: `eq` is false and `gt`/`gte`/`lt`/`lte` are false | Ordering a date against arbitrary text (`"2026-05-01"` vs `"May 1st"`) is meaningless; an ordinal result would look valid but isn't |
| Neither ISO | Ordinal, as today | Ordinary text comparisons don't change |
| JSON objects and arrays | `AreEqual` compares two `JsonElement`s with `JsonElement.DeepEquals` and two `JsonNode`s with `JsonNode.DeepEquals`. A `JsonElement` against a `JsonNode` is unequal. Objects and arrays can't be ordered | `changed Customer` and `eq` on object/array paths compare content. Current and previous normally come from the same source, so mixing the two JSON models isn't worth a conversion |
| `changed` | `!ValueComparer.AreEqual(current, previous)` instead of `!Equals(...)` | One comparison rule set (AGENTS.md: operators use `ValueComparer`). `100` vs `100.0` and the same instant in different offsets are no longer changes |
| Detecting ISO strings cheaply | A string is only parsed when it can be ISO (at least 10 characters, `yyyy-` prefix shape) | Every string-vs-string comparison now checks for dates; ordinary strings skip the parse |

## Breaking changes (`feat!:`)

- Two ISO 8601 strings now compare as instants: `"2026-01-01"` equals `"2026-01-01T00:00:00Z"`, and
  `"2026-05-01T14:00:00+02:00"` equals `"2026-05-01T12:00:00Z"` (both previously unequal).
- An ISO date string against a non-ISO string can't be ordered: `gt`/`gte`/`lt`/`lte` are false (previously ordinal).
- `changed` no longer reports a change for equal values of different types or formats (`100` vs `100.0`), for the same
  instant in different offsets, or for identical JSON objects and arrays.

## Test plan

**`ValueComparerTests`:** ISO pairs across offsets, date-only vs date-time, `Z` vs no offset, ordering of ISO pairs;
one-sided ISO pairs not equal and not orderable (both directions); non-ISO pairs still ordinal; strings shaped like
dates but invalid (`"2026-13-45"`) treated as non-ISO; `JsonElement` and `JsonNode` deep equality (equal content from
different documents, different content, property order ignored), mixed `JsonElement`/`JsonNode` unequal, JSON objects
not orderable.

**`ChangedOperatorTests`:** `100` vs `100.0m`, the same instant in different offsets, identical JSON objects from two
documents → no change; a changed nested value inside a JSON object → change; existing CLR cases unchanged.

**Integration:** a JSON rule on JSON state with a date range (`gte`/`lt` on ISO strings across offsets) and `changed`
on an unchanged object path.

## Docs

- JSON guide: the "Comparing Values" table gains ISO string pairs, one-sided ISO, and JSON objects/arrays; the
  "Rules on JSON State" section stops saying dates only work as `referencePath`.
- Upgrading guide: a "0.4 → 0.5" section with the breaking changes above.
- `ValueComparer` XML docs.
