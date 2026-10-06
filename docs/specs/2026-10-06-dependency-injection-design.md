# Design: Dependency Injection Extensions

**Issue:** [#5 Phase 1.2C: Dependency Injection Extensions](https://github.com/STotev/RuleForge/issues/5)
**Parent PRD:** [#1 Action Scheduling & Dependency Injection Support](https://github.com/STotev/RuleForge/issues/1)
**Date:** 2026-10-06
**Status:** Approved 2026-10-06 (HITL review; Q1 as recommended, Q2 and Q3 revised — see Resolved questions)
**Depends on:** #55 (action names by registration, PR #56)

---

## Problem

Wiring RuleForge by hand means constructing the operator list, `ConditionEvaluator`, an `IActionExecutorFactory` and
`RuleEngine`, and actions can't take constructor dependencies such as an email service or a scoped `DbContext`.
`RuleForge.Extensions.DependencyInjection` is an empty project.

## Usage

```csharp
services.AddRuleForge(rules => rules
    .AddAction<SendEmailAction>()            // "send-email"; may inject IEmailService, DbContext, ILogger<T>, ...
    .AddAction<SendEmailAction>("notify")    // the same action under another name
    .AddAction<LogAction>()                  // "log" (not registered by default)
    .AddOperator<ContainsOperator>()         // built-in operators are registered automatically
    .ReplaceOperator<StrictEqualsOperator>() // replaces the built-in operator with the same Name ("eq")
    .RemoveOperator("changed"));             // removes a built-in or previously added operator

// In a request / message-handler scope:
var engine = scope.ServiceProvider.GetRequiredService<RuleEngine>();
var result = await engine.ExecuteAsync("OrderCreated", order, rules, cancellationToken);

// Before saving a rule from a UI:
var validator = scope.ServiceProvider.GetRequiredService<RuleValidator>();
```

## Proposed API

```csharp
namespace Microsoft.Extensions.DependencyInjection;          // convention for AddXxx extensions

public static class RuleForgeServiceCollectionExtensions
{
    public static IServiceCollection AddRuleForge(this IServiceCollection services, Action<RuleForgeBuilder>? configure = null);
}

namespace RuleForge.Extensions.DependencyInjection;

public sealed class RuleForgeBuilder
{
    public IServiceCollection Services { get; }
    public RuleForgeBuilder AddAction<TAction>(string? name = null) where TAction : class, IActionExecutor;
    public RuleForgeBuilder AddOperator<TOperator>() where TOperator : class, IConditionOperator;
    public RuleForgeBuilder ReplaceOperator<TOperator>() where TOperator : class, IConditionOperator;
    public RuleForgeBuilder RemoveOperator(string name);
}

public sealed class ServiceProviderActionExecutorFactory : IActionExecutorFactory   // registered by AddRuleForge
```

And, in the core `RuleForge` package (useful without DI too, and used by `AddRuleForge`):

```csharp
namespace RuleForge.Operators;

public static class BuiltInOperators
{
    // New instances of eq, changed, and, or, not, gt, gte, lt, lte.
    public static IReadOnlyList<IConditionOperator> Create();
}
```

## Registrations (proposal — see Q1)

| Service | Implementation | Lifetime |
|---|---|---|
| `IConditionOperator` (each built-in and each `AddOperator<T>`) | the operator | **Singleton** |
| `IConditionEvaluator` | `ConditionEvaluator` (all registered operators) | **Singleton** |
| each `AddAction<T>` | `T` | **Scoped** |
| `IActionExecutorFactory` | `ServiceProviderActionExecutorFactory` | Scoped |
| `RuleEngine` | `RuleEngine` | Scoped |
| `RuleValidator` | `RuleValidator` | Scoped (it reads the factory's action types) |
| `TimeProvider` | `TimeProvider.System`, only if none is registered (`TryAdd`) | Singleton |

`AddRuleForge` can be called more than once (e.g. by libraries contributing actions); the core services are added
once (`TryAdd`) and every call adds its actions and operators.

## How the factory finds actions by name

Names belong to registrations (#55). `AddAction<T>(name)` records `name ?? ActionNames.GetDefaultName(typeof(T))`
→ `typeof(T)` in a singleton map when the services are configured, so the map is complete at startup and no action is
instantiated to learn its name. `TryGetExecutor("send-email")` (any case) resolves only that action's CLR type from
the current scope, giving a new instance per scope. `ActionNames` returns the map's keys. Two registrations under
names that differ only in case throw while configuring, with the same messages as `DirectActionExecutorFactory`. One
CLR type may be registered under several names.

## Operators: add, replace, remove

Operators keep their instance `Name`. The builder records operator registrations in order; when the evaluator is
first built (it's a singleton), the operators are created and:

- `RemoveOperator(name)` drops the operators registered **so far** (built-in or added) whose `Name` matches, ignoring case;
- `ReplaceOperator<T>()` adds `T` and drops any operator registered so far with the same `Name` as `T`;
- `AddOperator<T>()` adds `T`; a name clash with an operator that wasn't removed or replaced fails with the existing
  message (`Operator 'X' conflicts with 'x' …`).

## Decisions

| Question | Decision | Rationale |
|---|---|---|
| Namespace of `AddRuleForge` | `Microsoft.Extensions.DependencyInjection` | Standard convention: discoverable without an extra `using` |
| Built-in operator list | New `BuiltInOperators.Create()` in the core package | The one central list the roadmap asked for; the sample, guides and tests can use it too |
| `LogAction` | **Not** registered by default; `AddAction<LogAction>()` opts in | It writes to the console |
| Duplicate `AddAction<T>` / `AddOperator<T>` of the same CLR type | Registered once | Idempotent registration |
| Operator name conflicts | Fail when the evaluator is first resolved, with the existing message (`Operator 'X' conflicts with 'x' …`) | Same rule as without DI |
| Assembly scanning, keyed services, options/configuration binding | Out of scope | Can be added later without breaking this API |
| Tests | New `tests/RuleForge.Extensions.DependencyInjection.Tests` project using `Microsoft.Extensions.DependencyInjection` | Keeps the core test project free of DI packages |

## Resolved questions (reviewed 2026-10-06)

**Q1 — Lifetimes:** as recommended. Operators and `ConditionEvaluator` are Singleton; actions, the factory,
`RuleEngine` and `RuleValidator` are Scoped. This deliberately deviates from #5's text ("operators Scoped").

**Q2 — Action names:** neither lazy discovery nor another interface. Names moved to registrations (#55): a default
from `[ActionName]` or the class name in kebab-case, overridable with `AddAction<T>(name)`. Aliases/remapping later.
The rule JSON uses `"action"` (#55).

**Q3 — Built-in operators:** registered automatically, with `RemoveOperator(name)` and `ReplaceOperator<T>()` for
flexibility. Operators keep their instance `Name` (no registration-name override for operators for now).

## Breaking changes

None. New package contents and `BuiltInOperators` are additions; the non-DI path is unchanged.

## Test plan

- `AddRuleForge` registers the expected services with the expected lifetimes; calling it twice adds actions/operators
  without duplicating core services.
- A rule runs end to end through a `RuleEngine` resolved from a scope, using built-in and custom operators.
- An action with an injected service (a fake `IEmailService`) and an `ILogger<T>` executes; a new action instance per
  scope, the same within a scope.
- Factory: lookup by name in any case, unknown name, `ActionNames`, default and explicit names, one type under two
  names, duplicate/case-conflicting names fail while configuring.
- Operators: built-ins present by default; `AddOperator`, `ReplaceOperator`, `RemoveOperator`; conflicts fail.
- `RuleValidator` resolved from DI knows the registered actions and operators.
- `BuiltInOperators.Create()` returns all nine built-ins as new instances.

## Deviations

Recorded before merge (2026-10-06):

- **Operators aren't registered as individual `IConditionOperator` services.** The registrations table above listed
  them; instead, an internal singleton operator set is built once from the built-ins plus the builder's
  add/replace/remove calls, and shared by `ConditionEvaluator` and `RuleValidator`. Individual `IConditionOperator`
  registrations would make `RemoveOperator`/`ReplaceOperator` impossible to express. Custom operators are created with
  `ActivatorUtilities`, so they may take singleton dependencies.
- **`RemoveOperator`/`ReplaceOperator` apply in call order** (to operators registered *so far*), rather than to every
  matching operator regardless of order. `RemoveOperator("eq").AddOperator<MyEq>()` therefore keeps `MyEq`, which is
  the intuitive reading.
- **The test project uses `ILogger`-free fakes.** #5's text suggested `ILogger<T>`; an injected fake service proves the
  same thing without adding logging packages.
