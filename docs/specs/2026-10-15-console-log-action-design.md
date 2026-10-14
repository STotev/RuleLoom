# Design: Rename LogAction to ConsoleLogAction

**Issue:** [#78 Rename LogAction to ConsoleLogAction (console-log)](https://github.com/STotev/RuleForge/issues/78)
**Parent PRD:** [#66 JSON state and central-service spike (0.5)](https://github.com/STotev/RuleForge/issues/66) (spike follow-up)
**Date:** 2026-10-15
**Status:** Approved (AFK)

---

## Problem

The built-in `LogAction` (`log`) writes its `message` parameter to the console. It exists for development and testing,
but its name reads like a general logging action, so in the rules-service spike (#70) it looked like something
`AddRuleForge` should register.

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Name | `ConsoleLogAction`, so `console-log` by the naming convention | Says where the output goes; no `[ActionName]` needed |
| Registration | Still opt-in: `AddRuleForge` doesn't register it; `AddAction<ConsoleLogAction>()` or `DirectActionFactory([new ConsoleLogAction()])` does | Registered actions may later be listed in user-facing UIs (action catalogue); a development-only action shouldn't appear there by default |
| Behaviour | Unchanged (writes `message` to the console) | Rename only |
| Docs | XML docs say it's for development and testing | The purpose is visible in IntelliSense |

## Breaking changes (`feat!:`)

- `LogAction` is now `ConsoleLogAction`, and its default name is `console-log` instead of `log`.

Migration: rename the type, and change `"action": "log"` to `"console-log"` in rules, or keep the old name by
registering it explicitly: `AddAction<ConsoleLogAction>("log")` / `new DirectActionFactory().Add(new ConsoleLogAction(), "log")`.

## Test plan

- `ActionNamesTests`: `ConsoleLogAction` → `console-log`.
- `DirectActionFactoryTests` updated for the new type and name. Tests that use `"log"` only as an arbitrary action name
  (parser, validator) stay as they are.

## Docs

- Upgrading guide (0.4 → 0.5), quick start, JSON guide, README, `AGENTS.md`, XML doc examples; both samples use
  `console-log`.
