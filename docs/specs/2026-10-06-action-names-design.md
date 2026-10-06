# Design: Action Names by Registration

**Issue:** [#55 Name actions by registration](https://github.com/STotev/RuleForge/issues/55) (blocks #5)
**Parent PRD:** [#1 Action Scheduling & Dependency Injection Support](https://github.com/STotev/RuleForge/issues/1)
**Date:** 2026-10-06
**Status:** Approved (decided while reviewing #5's spec, PR #54)

---

## Problem

An action's name lives on the executor instance (`IAction.Type`). A DI container can't read it at startup,
because creating an action may need scoped services, and nobody but the author can choose the name, so two libraries
that both ship a `notify` action can't be used together. `Type` is also a confusing word for a name.

## Decisions

| Question | Decision | Rationale / prior art |
|---|---|---|
| Where the name lives | In the **registration** (factory / DI), not on the executor. `IAction` is just `ExecuteAsync` | The name → type map exists at startup; registrants control names |
| Default name | Class name, minus an `Action` suffix (unless the class is named exactly `Action`), generic arity removed, converted with `JsonNamingPolicy.KebabCaseLower`: `SendEmailAction` → `send-email`, `LogAction` → `log`, `HttpRequestAction` → `http-request` | Convention over configuration, as in ASP.NET Core MVC (controller = class name minus `Controller`) and MassTransit (`KebabCaseEndpointNameFormatter`, consumer name minus `Consumer`) |
| Author override | Optional `[ActionName("notify")]` on the class | Like ASP.NET Core's `[ActionName]`; for classes whose name doesn't fit |
| Registrant override | `DirectActionFactory.Add(executor, name)`; `AddAction<T>(name)` in #5 | Resolves conflicts between libraries |
| Precedence | registration name → `[ActionName]` → convention | Most specific wins |
| Helper | `ActionNames.GetDefaultName(Type)` (public, in Abstractions) | Shared by the non-DI factory, DI (#5) and anyone documenting their actions |
| Rule JSON | `"action": "send-email"` replaces `"type"`; `ActionDefinition.Type` → `ActionDefinition.Action` | Mirrors `"operator": "eq"` |
| Results | `ActionResult.ActionType` → `ActionName` | Avoids `result.Action` / `result.Type` ambiguity |
| Factory contract | `IActionFactory.TryGetAction(string name, …)`; `ActionTypes` → `ActionNames` | |
| `DirectActionFactory` | `new()` + fluent `Add(executor, name?)`; the existing constructor taking executors registers each under its default name | Keeps the one-line non-DI setup |
| Duplicates | Names differing only in case are rejected: `Action name 'notify' is registered more than once.` / `Action name 'Notify' conflicts with 'notify' (names are case-insensitive).` | Same rule as #37 |
| Aliases / remapping | Not now. Registration maps make them a later addition (several names → one executor) | Noted in #55 |

## Breaking changes (`feat!:`)

- `IAction.Type` is removed; delete it from custom actions (rename the class or add `[ActionName]` if the
  default name differs from the old `Type`).
- Rule JSON: `"type"` → `"action"` in action objects. `ActionDefinition.Type` → `Action`.
- `ActionResult.ActionType` → `ActionName`; `IActionFactory.ActionTypes` → `ActionNames`.
- `RuleValidator` and parser locations: `$.actions[i].type` → `$.actions[i].action`.

## Test plan

- `ActionNames.GetDefaultName`: suffix stripped, kebab-case, acronyms, no suffix, class named `Action`, generic
  types, `[ActionName]` precedence.
- `DirectActionFactory`: default names, explicit names, constructor with executors, case-insensitive lookup,
  duplicate/conflict messages, `ActionNames`.
- Parser and `RuleValidator`: `"action"` required, locations.
- Engine: `ActionResult.ActionName` stamped; existing engine and integration tests updated.
