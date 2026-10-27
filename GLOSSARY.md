# RuleLoom

RuleLoom runs business rules, authored as JSON, against events that an application raises.

## Language

**Rule**:
A named condition on one event type, with the actions to take when the condition holds.

**Catalog**:
The fixed vocabulary that one application's rules are written in: its event types (with the contexts they declare), context providers, operators and actions. Rules are written against a catalog, but they aren't part of it.
_Avoid_: Loom, registry, catalogue

**Catalog store**:
Where a catalog is kept between runs. It can also add event and context schemas that no code defines, but it can never redefine what the code defines.
_Avoid_: Schema store

**Catalog problem**:
A broken link in a store-contributed entry of a catalog, such as a context with no provider. It affects only the rules that use that entry.
_Avoid_: Catalog error, warning

**Event type**:
The name of something that happened in the application (`order-placed`), which a rule reacts to.

**Event schema**:
The description of an event type's fields and the contexts it declares.

**Context**:
Related data that an event type declares under a name (`@toAccount`), which rules read alongside the event.

**Context provider**:
The code, registered under its own name (`account`), that loads a kind of related data. One provider can back several contexts (`@fromAccount`, `@toAccount`).
_Avoid_: Context loader

**Operator**:
A named test that conditions are built from (`eq`, `changed`, `and`).

**Action**:
A named piece of application code that a rule runs when it matches.
_Avoid_: Executor

**Time zone**:
The zone, named by its IANA id (`Europe/Sofia`), in which a date-time that states no offset is read. UTC unless configured.
_Avoid_: Local time, server time

**Date-time**:
A moment in time. One that states an offset is that moment wherever it's read; one that doesn't is wall-clock time in the time zone.
_Avoid_: Timestamp, instant (in docs for rule authors)

**Date**:
A calendar day without a time of day (`2026-05-01`). It's never a moment in time, not even midnight.
_Avoid_: Midnight date

**Path**:
Where a rule reads a value: the event's current or previous state, or a context (`Total`, `$previous.Status`, `@customer.Tier`).
_Avoid_: Property path, selector

**Parameter**:
What a rule gives an action: a value (`"subject": "Welcome"`) or a path read when the action runs (`"to": { "$path": "@customer.Email" }`). Parameters are authored in rules and described by the action.
_Avoid_: Argument, option

**Setting**:
Configuration that the application gives an action (an API key, a base URL). Settings never appear in rules or in a catalog.
_Avoid_: Parameter, option
