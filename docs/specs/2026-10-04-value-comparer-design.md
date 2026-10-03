# Design: Shared Value Comparer, Used by `eq`

**Issue:** [#30 API hardening: shared value comparer, used by `eq`](https://github.com/STotev/RuleForge/issues/30)
**Parent PRD:** [#22 API hardening before 1.0](https://github.com/STotev/RuleForge/issues/22) (module 1)
**Date:** 2026-10-04
**Status:** Approved (AFK)

---

## Problem

`eq` compares with `object.Equals`. JSON rule values are parsed as `int`, `long`, `double`, `string` or `bool`,
while domain properties are often `decimal`, enums or dates. `Price eq 100` therefore never matches a
`decimal Price`, `Status eq "Active"` never matches an enum, and a date can never match its JSON string. Nothing
fails; the rule just silently doesn't fire.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Shape | Two public static classes in a new `RuleForge.Abstractions.Values` namespace: `ValueComparer` (equality, ordering) and `ValueConverter` (conversion) | PRD: one implementation of the rules, public so plugins use the same rules; not pluggable yet, so no interface or instance |
| Location | `RuleForge.Abstractions` | Operators (#34), parameters (#35) and scheduling (#33) all need it; Abstractions must stay dependency-free, which this is |
| Ordering API | `TryCompare(left, right, out int)` | "Not comparable" is a normal outcome (e.g. a number vs a non-numeric string), not an exception; ordering operators (#36) evaluate to `false` then |
| Numbers | Compare by value. If either side is `float`/`double`, compare as `double`; otherwise as `decimal` | `decimal` holds every integer type exactly; `double` covers the range `decimal` can't |
| Enums | Same enum type: by value. Enum vs string: by name, case-insensitive (including `[Flags]` combinations such as `"Read, Write"`); numeric strings never match. Enum vs number: by underlying value. Different enum types: not equal | PRD case policy: names are case-insensitive; values from JSON are names or numbers |
| Dates | `DateTime` and `DateTimeOffset` compare as UTC instants. `DateTime` with `Kind.Local` is converted; `Unspecified` is treated as UTC | Same UTC-only rule as scheduling |
| Date strings | When the other side is a date, a string is parsed as **strict ISO 8601**: `yyyy-MM-dd`, or `yyyy-MM-ddTHH:mm[:ss[.fffffff]]`, optionally followed by `Z` or `±hh:mm`. No offset means UTC; date-only means UTC midnight. Anything else never matches | No culture guessing (PRD). Revisit with the Schema System, which can type values at parse time |
| Strings | Ordinal, case-sensitive | PRD: only *names* ignore case, not values |
| Nulls | `null` equals only `null`; nulls are never ordered | Missing data behaves predictably |
| Everything else | Equality falls back to `object.Equals`. Ordering falls back to `IComparable` only when both values have the same type | Keeps `bool`, `Guid`, `TimeSpan`, etc. working without special cases |
| `changed` | Unchanged (still `object.Equals`) | Both sides come from the same property, so they always have the same CLR type. `changed` is rewritten in #34 |

## API

```csharp
namespace RuleForge.Abstractions.Values;

public static class ValueComparer
{
    // Equality under the rules above.
    public static bool AreEqual(object? left, object? right);

    // Ordering under the rules above; false when the values can't be ordered.
    public static bool TryCompare(object? left, object? right, out int result);
}

public static class ValueConverter
{
    // Converts to targetType under the same rules; false when it can't.
    public static bool TryConvert(object? value, Type targetType, out object? result);
    public static bool TryConvert<T>(object? value, [MaybeNullWhen(false)] out T result);
}
```

### Conversion rules (`ValueConverter`)

| Target | Accepted sources |
|---|---|
| Any | `null` → reference or `Nullable<T>` targets only; a value already assignable to the target is returned as-is |
| `Nullable<T>` | Whatever converts to `T` |
| Numeric types | Any numeric value that fits the target **without loss** (no overflow, no dropped fraction) |
| Enum | Name string (case-insensitive, `[Flags]` combinations allowed); integral number |
| `DateTimeOffset` / `DateTime` | The other date type; strict ISO 8601 string. `DateTime` results have `Kind.Utc` |

Other combinations (e.g. number → string, `"100"` → `int`) are deliberately not converted here. #35 decides
whether action parameters need more.

`ValueComparer` uses `ValueConverter` for enum names and date strings, so both classes follow one set of rules.

## Engine behaviour

`EqualsOperator` calls `ValueComparer.AreEqual`. Rules that previously failed to match because of a type
mismatch now match. Nothing that matched before stops matching, apart from `NaN`, which no longer equals
itself.

## Test plan

- `ValueComparerTests`: equality and ordering matrices covering numeric type pairs, `float`/`double`
  precision, enums (name, case, number, flags, numeric strings, different enum types), dates (`Kind`
  handling, offsets, ISO strings with and without offset, date-only, non-ISO), strings, nulls, `NaN`,
  `IComparable` fallback, and incomparable pairs.
- `ValueConverterTests`: each target row above, lossless numeric conversion, failures.
- `EqualsOperatorTests`: cross-type cases through the evaluator.
- Integration: a JSON rule `Price eq 100` matches a `decimal` price and runs its action end to end.

## Out of scope

- Ordering operators (`gt`, …) — #36. They will use `TryCompare`.
- Exposing the comparer through `OperatorContext` — #34.
- Schedule `ReferencePath` strings — #33 switches them to `ValueConverter`.
- A pluggable comparer (`IValueComparer`) — PRD Out of Scope.
- `DateOnly` / `TimeOnly` — fall back to `object.Equals` for now.
