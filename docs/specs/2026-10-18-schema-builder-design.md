# Design: Schema Builder and JSON Schema Export (RuleLoom.Schemas)

**Issue:** [#92 Schema builder and JSON Schema export (RuleLoom.Schemas)](https://github.com/STotev/RuleLoom/issues/92)
**Parent PRD:** [#87 Schemas and contexts (0.6)](https://github.com/STotev/RuleLoom/issues/87) (user stories 1–17, 22)
**Date:** 2026-10-18
**Status:** Approved (HITL, decisions below)

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

        Context<CustomerDto>("customer", c =>
        {
            c.Field(x => x.Tier).Label("Loyalty tier");
            c.Field(x => x.InternalId).Ignore();
        }).Label("Customer");
        Context<UserDto>("watchers").Many().Label("Watchers");
    }
}

EventSchemaDefinition schema = new OrderPlacedSchema().Build();

var schemas = new EventSchemaCollection().AddFromAssemblyOf<OrderPlacedSchema>();
string json = schemas.ToJson();                                        // a JSON array of event documents
IReadOnlyList<EventSchemaDefinition> parsed = EventSchemaDefinition.ParseMany(json);
```

- **`Event(name)`** is required; building without it throws. `Label`/`Description` describe the event.
- **`Field(expression)`** points at a property, at any depth (`o => o.Customer.Tier`), and configures `Label`,
  `Description` or `Ignore()`. Configuring the same field twice merges (the last value wins). An expression that isn't a
  property chain throws.
- **`Context<T>(name, configure?)`** declares a context (names ignore case; duplicates throw). Its fields are inferred
  from `T` and can be configured with the same `Field(...)` API in `configure`; `.Many()` marks a collection of `T`;
  `Label`/`Description` describe it. Contexts are descriptions only: the engine
  loads them through context providers (#91).
- The configuration runs in the constructor (FluentValidation style). `Build()` (also used by the collection) runs
  inference, applies the configuration and returns an `EventSchemaDefinition`. Configuring a field that inference didn't
  produce (e.g. below a cycle or inside a dictionary) throws.
- **`EventSchemaCollection`** gathers schema classes (`Add<TSchema>()`, `Add(schema)`, `AddFromAssemblyOf<T>()` /
  `AddFromAssembly(assembly)`: public non-abstract classes with a parameterless constructor), builds them (duplicate
  event types throw) and writes them as JSON.
- Default labels: the event label comes from the event type (`OrderPlaced` → "Order placed"), a context label from its
  name (`customer` → "Customer").

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
  fields, so schemas stay finite. Producers can `Ignore()` the property instead.
- Property names keep their CLR spelling; rule paths match them ignoring case.

## Model

Immutable records built by the builder and read back from JSON:

- **`EventSchemaDefinition`**: `EventType`, `Label`, `Description`, `Fields`, `Contexts`; `ToJson()`, `Parse(json)`,
  `ParseMany(json)`.
- **`FieldDefinition`**: `Name`, `Type` (`FieldType`), `IsNullable`, `Label`, `Description`, `EnumValues`, `Fields`
  (objects), `Items` (arrays: the element's definition).
- **`ContextDefinition`**: `Name`, `Label`, `Description`, `IsMany`, `Fields`.

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
- Reading JSON back accepts what the serializer writes; other keywords are ignored, and `$ref` isn't supported. A
  schema round-trips without loss: `Parse(schema.ToJson()).ToJson()` equals `schema.ToJson()`. Invalid documents (no
  `x-ruleloom-event`, unknown `type`) throw `JsonException`.
- **One document per event.** A collection writes a JSON array of event documents; `ParseMany` reads it back.

## Test plan

- **Builder:** event name required, labels and descriptions, nested `Field` paths, `Ignore`, merging repeated
  configuration, invalid expressions, contexts (single and many, duplicates), assembly collection and individual adds.
- **Inference:** every row of the type table, nullability (reference and value types), labels from names including
  acronyms, nested objects, arrays of primitives and objects, dictionaries, cycles.
- **JSON:** the output shape (keywords, `title`, `format`, nullable types, contexts), lossless round trip, parsing ignores
  unknown keywords.

## Decisions from review

| Question | Decision |
|---|---|
| Names | `EventSchema<T>` is the producer's base class; the built model is `EventSchemaDefinition` (`FieldDefinition`, `ContextDefinition`) |
| Several events | One JSON Schema document per event; a collection is a JSON array of documents |
| Context fields | Configurable with a nested builder: `Context<T>(name, c => c.Field(...))` |
| Cycles | Stop at the repeated type (object without fields) |
