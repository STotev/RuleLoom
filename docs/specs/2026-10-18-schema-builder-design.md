# Design: Schema Builder and JSON Schema Export (RuleLoom.Schemas)

**Issue:** [#92 Schema builder and JSON Schema export (RuleLoom.Schemas)](https://github.com/STotev/RuleLoom/issues/92)
**Parent PRD:** [#87 Schemas and contexts (0.6)](https://github.com/STotev/RuleLoom/issues/87) (user stories 1–17, 22)
**Date:** 2026-10-18
**Status:** Draft (HITL: open questions below)

---

## Problem

A central rules service doesn't know the events it handles: their fields, types and meaning. Producers already have
DTOs for their events, so they should describe each event in a few lines, with everything else inferred, and ship the
description as a standard document that the central service (and later a UI) can read.

## Package

New **`RuleLoom.Schemas`** package (namespace `RuleLoom.Schemas`), with no dependencies beyond the BCL (reflection,
System.Text.Json). Producers reference only this package: no engine, no DI. Tests go in a new
`RuleLoom.Schemas.Tests` project.

## Defining a schema

```csharp
public sealed class OrderPlacedSchema : EventSchema<OrderDto>
{
    public OrderPlacedSchema()
    {
        Event("OrderPlaced").Label("Order placed").Description("A customer placed an order.");

        Field(o => o.Total).Label("Order total");
        Field(o => o.Customer.Tier).Description("Loyalty tier at the time of the order.");
        Field(o => o.InternalNotes).Ignore();

        Context<CustomerDto>("customer").Label("Customer");
        Context<UserDto>("watchers").Many().Label("Watchers");
    }
}

var schemas = new EventSchemaCollection().AddFromAssemblyOf<OrderPlacedSchema>();
string json = schemas.ToJson();
```

- **`Event(name)`** is required; building without it throws. `Label`/`Description` describe the event.
- **`Field(expression)`** points at a property, at any depth (`o => o.Customer.Tier`), and configures `Label`,
  `Description` or `Ignore()`. Configuring the same field twice merges (the last value wins). An expression that isn't a
  property chain throws.
- **`Context<T>(name)`** declares a context (names ignore case; duplicates throw). Its fields are inferred from `T`;
  `.Many()` marks a collection of `T`; `Label`/`Description` describe it. Contexts are descriptions only: the engine
  loads them through context providers (#91).
- The configuration runs in the constructor (FluentValidation style). The class is turned into a schema by `Build()`
  (used by the collection), which runs inference and applies the configuration.

## Inference

For the subject type and each context type, from public instance properties with a getter:

| CLR type | Field type | JSON Schema |
|---|---|---|
| `string`, `char`, `Guid`, `Uri`, `TimeSpan` | String | `"type": "string"` |
| `int`, `long`, `short`, `byte` (and unsigned) | Integer | `"type": "integer"` |
| `decimal`, `double`, `float` | Number | `"type": "number"` |
| `bool` | Boolean | `"type": "boolean"` |
| `DateTime`, `DateTimeOffset` | DateTime | `"type": "string", "format": "date-time"` |
| `DateOnly` | Date | `"type": "string", "format": "date"` |
| enums | Enum (names) | `"type": "string", "enum": [...]` |
| arrays, `IEnumerable<T>` (not `string`) | Array of `T` | `"type": "array", "items": {...}` |
| dictionaries | Object without fields | `"type": "object"` |
| other classes/structs/records | Object with nested fields | `"type": "object", "properties": {...}` |

- **Nullability** from nullable annotations (`string?`, `int?`); nullable fields add `"null"` to `type`.
- **Labels** from property names: `CustomerTier` → "Customer tier", `VatNumber` → "Vat number", `VATNumber` →
  "VAT number". Explicit `Label(...)` wins.
- **Cycles** (a type that contains itself, directly or through other types): the repeated type becomes an object without
  fields, so schemas stay finite. *(See open question 4.)*
- Property names keep their CLR spelling; rule paths match them ignoring case.

## Model

Immutable records built by the builder and read back from JSON:

- **`EventSchema`**: `EventType`, `Label`, `Description`, `Fields`, `Contexts`. *(Name: see open question 1.)*
- **`FieldSchema`**: `Name`, `Type` (`FieldType`), `IsNullable`, `Label`, `Description`, `EnumValues`, `Fields` (objects),
  `Items` (arrays).
- **`ContextSchema`**: `Name`, `Label`, `Description`, `IsMany`, `Fields`.

## JSON format

Each event is a **JSON Schema 2020-12** document for its subject, with RuleLoom keywords for what JSON Schema doesn't
cover. Labels use the standard `title`, descriptions `description`.

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "x-ruleloom-event": "OrderPlaced",
  "title": "Order placed",
  "description": "A customer placed an order.",
  "type": "object",
  "properties": {
    "Total": { "type": "number", "title": "Order total" },
    "Status": { "type": "string", "enum": ["Placed", "Paid"], "title": "Status" },
    "PlacedAt": { "type": "string", "format": "date-time", "title": "Placed at" },
    "Note": { "type": ["string", "null"], "title": "Note" },
    "Customer": { "type": "object", "title": "Customer", "properties": { "Tier": { "type": "string", "title": "Tier" } } },
    "Lines": { "type": "array", "title": "Lines", "items": { "type": "object", "properties": { "Sku": { "type": "string", "title": "Sku" } } } }
  },
  "x-ruleloom-contexts": {
    "customer": { "title": "Customer", "type": "object", "properties": { "Tier": { "type": "string", "title": "Tier" } } },
    "watchers": { "title": "Watchers", "type": "array", "items": { "type": "object", "properties": { "Email": { "type": "string", "title": "Email" } } } }
  }
}
```

- A context is itself a JSON Schema (an object, or an array of objects for `Many()`).
- Ignored fields are left out.
- Reading JSON back accepts what the serializer writes; other keywords are ignored, and `$ref` isn't supported. A schema
  round-trips without loss: `Parse(ToJson(schema))` equals `schema`.
- *(Several events in one document: see open question 2.)*

## Test plan

- **Builder:** event name required, labels and descriptions, nested `Field` paths, `Ignore`, merging repeated
  configuration, invalid expressions, contexts (single and many, duplicates), assembly collection and individual adds.
- **Inference:** every row of the type table, nullability (reference and value types), labels from names including
  acronyms, nested objects, arrays of primitives and objects, dictionaries, cycles.
- **JSON:** the output shape (keywords, `title`, `format`, nullable types, contexts), lossless round trip, parsing ignores
  unknown keywords.

## Open questions (for review)

1. **Names of the builder base class and the model.** `EventSchema<T>` (builder) and `EventSchema` (model) differ only in
   arity, which may confuse.
2. **Several events in one JSON document.** One document per event, or one file with all events?
3. **Configuring a context's fields** (labels, ignore) in 0.6, or inference only for contexts?
4. **Cycles**: stop at the repeated type (object without fields), or fail the build so the producer decides?
