# Design: Typed Actions

**Issue:** [#115 Typed actions and their parameters in the catalog](https://github.com/STotev/RuleLoom/issues/115)
**Parent PRD:** [#113 Typed actions and authoring endpoints (0.7)](https://github.com/STotev/RuleLoom/issues/113) (user stories 56–63, 73)
**Date:** 2026-10-26
**Status:** Implemented

---

## Problem

An action reads its parameters one by one (`action.GetParameter<string>("subject")`), and nothing describes them: the
catalog, and so an authoring UI or the validator, can't tell which parameters an action takes, their types, or which are
required. This slice lets an action receive its parameters as a typed object and puts their schema in the catalog.
Paths in parameters (`$path`, #116) and validating parameters (#117) build on it.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Contract | `IAction<TParameters> : IAction` with `ExecuteAsync(TParameters parameters, ActionDefinition action, EvaluationContext context, CancellationToken)`; a default interface member implements `IAction.ExecuteAsync` by calling `action.GetParameters<TParameters>(context)` | The engine and factories keep calling `IAction`; no base class (`Action<T>` collides with `System.Action<T>`) |
| Binding | `ActionDefinition.GetParameters<T>(EvaluationContext context)` serializes `Parameters` and deserializes them as `T` | One rule set (System.Text.Json) for binding and for the schema |
| Binding options | `ParametersSchemaDefinition.JsonOptions` (read-only): Web defaults (names ignore case, camelCase), enums by name, required constructor parameters and nullable annotations respected, numbers not read from strings. `UseJsonOptions` doesn't apply | Parameters are rule JSON, not the app's data; what the schema says is required is exactly what binding requires |
| Binding failure | `InvalidOperationException` naming the action and the parameter (`Parameter 'subject' of action 'send-email' can't be read as SendEmailParameters: …`), or the whole parameters for a missing required one; the engine reports it as that action's failure | Fails only that action, like other action exceptions |
| `context` argument | Required now, used by #116 to resolve paths | The signature doesn't change when paths arrive |
| Describing parameters | `ActionDescriptor<TAction, TParameters> : ActionDescriptor<TAction> where TAction : IAction<TParameters>` with `Parameter(p => p.X)` returning the `FieldBuilder` (`Label`, `Description`, `Ignore`) | A mismatched descriptor doesn't compile; it's found next to the action like an untyped descriptor |
| Schema type | `ParametersSchema<T>` (in `RuleLoom.Schemas`: `Parameter(...)`, `Build()`) generates a `ParametersSchemaDefinition : SchemaDefinition` (`Fields`, `ToJson`, `Parse`) | Same generator as events and contexts; the descriptor (in Abstractions) and the builder (inferred schemas) both use it |
| Required | From the type: constructor parameters without defaults and `required` members, written as JSON Schema `required` | As System.Text.Json reads them with the binding options |
| Without a descriptor | A typed action is described by its parameters type alone; an untyped descriptor of a typed action adds only the label | Story 60 |
| Two parameter types | An action implementing `IAction<A>` and `IAction<B>` fails the catalog's build | The catalog never guesses |
| Catalog | `ActionRegistration.Parameters` and `CatalogAction.Parameters` (`ParametersSchemaDefinition?`, `null` for untyped actions); the document writes `"parameters": { JSON Schema }` per typed action and reads it back | Story 73 |
| `IActionDescriptor` | Gains `BuildParameters()` (`null` unless the descriptor describes parameters) | How the builder reads a typed descriptor's configuration |

## Example

```csharp
public sealed record SendEmailParameters(string To, string Subject, string? Template = null);

public sealed class SendEmailAction(IEmailSender sender) : IAction<SendEmailParameters>
{
    public async Task<ActionResult> ExecuteAsync(SendEmailParameters parameters, ActionDefinition action,
        EvaluationContext context, CancellationToken cancellationToken = default)
    {
        await sender.SendAsync(parameters.To, parameters.Subject, cancellationToken);
        return ActionResult.Success();
    }
}

public sealed class SendEmailDescriptor : ActionDescriptor<SendEmailAction, SendEmailParameters>
{
    public SendEmailDescriptor()
    {
        Label("Send email");
        Parameter(p => p.To).Label("Recipient");
    }
}
```

```json
{ "name": "send-email", "label": "Send email",
  "parameters": { "$schema": "https://json-schema.org/draft/2020-12/schema", "type": "object",
    "properties": { "to": { "type": "string", "title": "Recipient" }, "subject": { "type": "string" },
      "template": { "type": ["string", "null"], "default": null } },
    "required": ["to", "subject"] } }
```

## Deviations

- **`GetParameter<T>(name, context)` moves to #116.** Until parameters can be paths, it would only repeat
  `GetParameter<T>(name)`; it arrives with path resolution, where the context is needed.
