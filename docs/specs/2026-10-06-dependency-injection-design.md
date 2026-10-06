# Design: Dependency Injection Extensions

**Issue:** [#5 Phase 1.2C: Dependency Injection Extensions](https://github.com/STotev/RuleForge/issues/5)
**Parent PRD:** [#1 Action Scheduling & Dependency Injection Support](https://github.com/STotev/RuleForge/issues/1)
**Date:** 2026-10-06
**Status:** Draft — awaiting maintainer review (HITL)

---

## Problem

Wiring RuleForge by hand means constructing the operator list, `ConditionEvaluator`, an `IActionExecutorFactory` and
`RuleEngine`, and actions can't take constructor dependencies such as an email service or a scoped `DbContext`.
`RuleForge.Extensions.DependencyInjection` is an empty project.

## Usage

```csharp
services.AddRuleForge(rules => rules
    .AddAction<SendEmailAction>()        // may inject IEmailService, DbContext, ILogger<T>, ...
    .AddAction<LogAction>()
    .AddOperator<ContainsOperator>());   // built-in operators are registered automatically (Q3)

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
    public RuleForgeBuilder AddAction<TAction>() where TAction : class, IActionExecutor;
    public RuleForgeBuilder AddOperator<TOperator>() where TOperator : class, IConditionOperator;
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

Rules refer to actions by `IActionExecutor.Type` (`"send-email"`), an **instance** property, but registration only
knows the CLR type, and an action can't be created at startup because it may depend on scoped services (Q2).

Proposal: the factory builds the name → CLR type map **lazily, once per application**. The first time any scope needs
it, it resolves each registered action type once in that scope, reads `Type`, and caches the map in a singleton. After
that, `TryGetExecutor("send-email")` resolves only `SendEmailAction`, a new instance per scope. Duplicate names
(including names differing only in case, #37) throw with both CLR types in the message, like
`DirectActionExecutorFactory`. `ActionTypes` returns the cached names.

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

## Open questions for review

**Q1 — Lifetimes for operators and the evaluator.**
#5 and PRD #1 say operators are Scoped. *Recommendation: Singleton* for operators and `ConditionEvaluator`. Operators
are stateless, and a scoped evaluator would rebuild its operator dictionary on every request. Actions stay Scoped (they
may need a `DbContext`), and so do the factory, `RuleEngine` and `RuleValidator`. The alternative is to follow the
issue literally (everything Scoped); it costs a dictionary build per scope and allows operators with scoped
dependencies, which no current operator needs.

**Q2 — How the factory learns each action's name.**
*Recommendation: lazy discovery* as described above: no extra registration input, and `Type` stays the single source
of the name. Its costs: the first scope creates one instance of every registered action, and a constructor that throws
fails that first lookup. Alternatives:
- the name at registration: `AddAction<SendEmailAction>("send-email")`, explicit but duplicates `Type` and can drift;
- an attribute `[ActionType("send-email")]` on the class, explicit and static but also duplicates `Type`;
- `static abstract string ActionType` on `IActionExecutor` (C# 11), compile-time safe but a breaking change for every
  action, also without DI.

**Q3 — Should `AddRuleForge` register the built-in operators automatically?**
*Recommendation: yes*, so `AddRuleForge()` alone gives a working `eq`/`and`/`gt`/… evaluator, with `AddOperator<T>`
adding custom ones. The alternative is opt-in (`rules.AddBuiltInOperators()`), which is more explicit but easy to
forget.

## Breaking changes

None. New package contents and `BuiltInOperators` are additions; the non-DI path is unchanged.

## Test plan

- `AddRuleForge` registers the expected services with the expected lifetimes; calling it twice adds actions/operators
  without duplicating core services.
- A rule runs end to end through a `RuleEngine` resolved from a scope, using built-in and custom operators.
- An action with an injected service (a fake `IEmailService`) and an `ILogger<T>` executes; a new action instance per
  scope, the same within a scope.
- Factory: lookup by name in any case, unknown name, `ActionTypes`, duplicate action names fail with both types named.
- `RuleValidator` resolved from DI knows the registered actions and operators.
- `BuiltInOperators.Create()` returns all nine built-ins as new instances.
