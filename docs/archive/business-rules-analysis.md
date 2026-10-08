# RuleForge Project - Analysis & Recommendations

## 📋 Summary

**RuleForge** is a .NET library for building event-driven, configurable rule engines. It allows defining business rules externally (via JSON) and executing them against events with current/previous state tracking.

### Core Capabilities:
- **Event-driven execution**: Rules are triggered by named events
- **Flexible conditions**: Tree-based condition system with pluggable operators (eq, changed, and, or, not)
- **State comparison**: Supports comparing current vs. previous state
- **Dynamic actions**: Execute configurable actions when conditions are met
- **JSON-based rules**: Define rules externally without code changes
- **Path resolution**: Access nested properties via dot notation (e.g., `User.Address.ZipCode`)
- **Performance-optimized**: Uses compiled expression trees for property access and System.Text.Json for parsing

### Architecture:
```
┌─────────────────────────────────────────┐
│   RuleForge.Abstractions            │ ← Contracts only, no dependencies
│   - Interfaces & DTOs                    │
│   - RuleDefinition, ConditionNode        │
└─────────────────────────────────────────┘
                    ↑
┌─────────────────────────────────────────┐
│   RuleForge.Core                     │
│   - RuleEngine                           │
│   - ConditionEvaluator                   │
│   - Built-in operators & actions         │
└─────────────────────────────────────────┘
                    ↑
┌─────────────────────────────────────────┐
│   RuleForge.Extensions.DI            │
│   - Service registration                 │
└─────────────────────────────────────────┘
```

### Key Components:

1. **RuleEngine**: Orchestrates rule execution
    - Filters rules by event type
    - Evaluates conditions
    - Executes actions sequentially

2. **ConditionEvaluator**: Evaluates condition trees
    - Supports PathOperand (property references)
    - Supports ValueOperand (literal values)
    - Supports OperatorCondition (logical operations)

3. **PathResolver**: Resolves property paths using compiled expressions
    - Cached for performance
    - Supports nested paths

4. **RuleJsonParser**: Parses JSON into strongly-typed rules
    - Polymorphic deserialization
    - Type detection based on JSON properties

5. **Operators**: Pluggable condition operators
    - eq, changed, and, or, not
    - Easy to extend

6. **Actions**: Pluggable action executors
    - Currently: LogAction
    - Can have per-action conditions

---

## ✅ Strengths

1. **Clean separation of concerns**: Abstractions layer is dependency-free
2. **Extensibility**: Easy to add new operators and actions
3. **Type safety**: Strong typing throughout
4. **Performance**: Compiled expressions and efficient JSON parsing
5. **Testability**: Well-structured for unit testing
6. **Domain-agnostic**: Can be used in any domain
7. **Simple API**: Easy to understand and use

---

## 🚨 Issues & Improvement Suggestions

### 1. **Critical: Rule Filtering & Priority Issues**

**Problem**: RuleEngine doesn't sort by priority and lacks proper rule filtering options.

```csharp
// Current implementation
var matchingRules = rules.Where(r => r.EventType == eventType);
foreach (var rule in matchingRules) { ... }
```

**Recommendation**:
```csharp
var matchingRules = rules
    .Where(r => r.EventType == eventType)
    .OrderByDescending(r => r.Priority) // Priority handling
    .ToList();
```

**Additional considerations**:
- Should higher priority stop lower priority execution?
- Consider adding `StopOnMatch` or `ExecutionMode` enum to RuleDefinition

---

### 2. **Architecture: Missing Rule Storage Abstraction**

**Problem**: Rules are passed directly to `ExecuteAsync()`. No abstraction for rule storage/retrieval.

**Current**:
```csharp
await engine.ExecuteAsync(eventType, current, previous, rules);
```

**Recommendation**: Add IRuleRepository
```csharp
public interface IRuleRepository
{
    Task<IReadOnlyList<RuleDefinition>> GetRulesAsync(string eventType);
    Task<IReadOnlyList<RuleDefinition>> GetAllRulesAsync();
    Task AddRuleAsync(RuleDefinition rule);
    Task UpdateRuleAsync(RuleDefinition rule);
    Task RemoveRuleAsync(string ruleId);
}

// In-memory implementation
public class InMemoryRuleRepository : IRuleRepository { ... }

// Usage
public class RuleEngine
{
    private readonly IRuleRepository _ruleRepository;

    public async Task ExecuteAsync(string eventType, object current, object? previous)
    {
        var rules = await _ruleRepository.GetRulesAsync(eventType);
        // ... rest of logic
    }
}
```

**Benefits**:
- Cleaner API
- Easier to add persistent storage (SQL, Redis, etc.)
- Enables rule caching/indexing by event type

---

### 3. **Design Issue: Action Conditions Evaluated Inside Loop**

**Problem**: Actions have optional conditions evaluated during execution. This breaks single responsibility.

**Current**:
```csharp
foreach (var action in rule.Actions)
{
    if (action.Condition is not null && !_conditionEvaluator.Evaluate(context, action.Condition))
        continue;
    // execute action
}
```

**Recommendation**: Consider separating action conditions from action parameters OR making actions first-class rules.

**Option A**: Keep it simple (current approach is fine for most cases)

**Option B**: Make actions separate rules with dependencies:
```csharp
public class RuleDefinition
{
    public List<string> TriggerActionRuleIds { get; init; } // Chain to other rules
}
```

**Verdict**: Current approach is acceptable for v1. Consider Option B if complexity increases.

---

### 4. **Missing: Operator Validation**

**Problem**: No validation that operators receive correct operand counts/types.

**Example**: `eq` expects 2 operands, but nothing enforces this.

**Recommendation**:
```csharp
public interface IConditionOperator
{
    string Name { get; }
    int MinOperands { get; }
    int MaxOperands { get; }
    bool Evaluate(...);
}

// In ConditionEvaluator
if (op.Operands.Count < @operator.MinOperands ||
    op.Operands.Count > @operator.MaxOperands)
{
    throw new InvalidOperationException(
        $"Operator '{op.Operator}' expects {operator.MinOperands}-{operator.MaxOperands} operands, got {op.Operands.Count}");
}
```

---

### 5. **Missing: Error Handling & Observability**

**Problem**: No structured error handling, logging, or telemetry.

**Issues**:
- Property resolution failures throw exceptions
- No logging of rule executions
- No metrics/tracing

**Recommendation**:
```csharp
public interface IRuleExecutionObserver
{
    void OnRuleEvaluationStarted(string ruleId, string eventType);
    void OnRuleConditionResult(string ruleId, bool result);
    void OnActionExecuted(string ruleId, string actionType);
    void OnError(string ruleId, Exception ex);
}

public class RuleEngine
{
    private readonly ILogger<RuleEngine> _logger;
    private readonly IRuleExecutionObserver? _observer;

    public async Task ExecuteAsync(...)
    {
        foreach (var rule in matchingRules)
        {
            try
            {
                _observer?.OnRuleEvaluationStarted(rule.Id, rule.EventType);
                // ... execute rule
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rule {RuleId} failed", rule.Id);
                _observer?.OnError(rule.Id, ex);
                // Continue with next rule or rethrow?
            }
        }
    }
}
```

**Alternative**: Use Activity/OpenTelemetry for distributed tracing.

---

### 6. **Design: EvaluationContext Coupling**

**Problem**: `EvaluationContext` has both data (`Current`, `Previous`) and logic (`Resolve` method with `PathResolver`).

**Recommendation**: Separate concerns:

```csharp
// Data-only context
public class EvaluationContext
{
    public object? Current { get; init; }
    public object? Previous { get; init; }
    public IDictionary<string, object>? Metadata { get; init; }
}

// Separate resolver service
public interface IPathResolver
{
    object? Resolve(EvaluationContext context, string path);
}
```

**Benefits**:
- Better testability
- Easier to mock
- Clearer separation of data/behavior

---

### 7. **Missing: Expression/Function Support**

**Problem**: Cannot perform calculations or transformations in conditions.

**Example use case**: Check if `Price * 1.2 > 100`

**Current workaround**: Create custom operators for each operation.

**Recommendation**: Consider adding expression support (future feature):

```csharp
public class ExpressionOperand : ConditionNode
{
    public required string Expression { get; init; }
    // Parse "Price * 1.2" using something like DynamicExpresso or NCalc
}
```

**Caution**: Adds complexity and security concerns. May want to sandbox expressions.

---

### 8. **Testing: Missing Integration Tests**

**Current**: Only unit tests for parsing
**Missing**: End-to-end tests of RuleEngine execution

**Recommendation**:
```csharp
[Fact]
public async Task RuleEngine_CompleteFlow_ExecutesCorrectly()
{
    // Arrange
    var rules = RuleJsonParser.ParseMany("""
        [{
            "name": "Price Change Alert",
            "eventType": "ProductUpdated",
            "priority": 1,
            "condition": {
                "operator": "changed",
                "operands": [{ "path": "Price" }]
            },
            "actions": [{
                "type": "log",
                "parameters": { "message": "Price changed!" }
            }]
        }]
    """);

    var engine = CreateRuleEngine();
    var oldState = new Product { Price = 100 };
    var newState = new Product { Price = 200 };

    // Act
    await engine.ExecuteAsync("ProductUpdated", newState, oldState, rules);

    // Assert
    // Verify log action was called
}
```

---

### 9. **Performance: Consider Rule Indexing**

**Current**: Linear scan through all rules for each event.

**Problem**: O(n) complexity for large rule sets.

**Recommendation**: Index rules by event type:

```csharp
public class IndexedRuleRepository : IRuleRepository
{
    private readonly Dictionary<string, List<RuleDefinition>> _rulesByEventType = new();

    public Task<IReadOnlyList<RuleDefinition>> GetRulesAsync(string eventType)
    {
        if (_rulesByEventType.TryGetValue(eventType, out var rules))
            return Task.FromResult<IReadOnlyList<RuleDefinition>>(rules);
        return Task.FromResult<IReadOnlyList<RuleDefinition>>(Array.Empty<RuleDefinition>());
    }
}
```

---

### 10. **Missing: Rule Validation on Load**

**Problem**: Invalid rules only fail at runtime.

**Recommendation**: Add validation:

```csharp
public interface IRuleValidator
{
    ValidationResult Validate(RuleDefinition rule);
}

public class ValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = [];
}

public class DefaultRuleValidator : IRuleValidator
{
    private readonly IEnumerable<IConditionOperator> _operators;
    private readonly IEnumerable<IActionExecutor> _actions;

    public ValidationResult Validate(RuleDefinition rule)
    {
        var errors = new List<string>();

        // Check if operators exist
        // Check operand counts
        // Check action types exist
        // Check for circular dependencies

        return new ValidationResult {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }
}
```

---

### 11. **Missing: Async Action Support**

**Current**: Actions can be async but execution is sequential.

**Consideration**: Should actions run in parallel?

```csharp
// Current (sequential)
foreach (var action in rule.Actions)
{
    await executor.ExecuteAsync(action, context);
}

// Parallel option
await Task.WhenAll(rule.Actions
    .Where(a => /* condition check */)
    .Select(a => _actions[a.Type].ExecuteAsync(a, context)));
```

**Trade-offs**:
- Parallel: Faster but harder to debug, potential race conditions
- Sequential: Predictable, easier to reason about

**Recommendation**: Keep sequential by default, add `ParallelActions` flag to RuleDefinition if needed.

---

### 12. **API Design: Consider Fluent Rule Builder**

**Current**: Rules defined in JSON or manually constructed.

**Recommendation**: Add fluent API for programmatic rule creation:

```csharp
var rule = RuleBuilder.Create()
    .Named("High Price Alert")
    .ForEvent("ProductUpdated")
    .WithPriority(1)
    .When(c => c.Path("Price").Changed())
    .Execute(a => a.Log("Price changed!"))
    .Build();
```

**Benefits**:
- Better IDE support (IntelliSense)
- Compile-time validation
- More discoverable API

---

### 13. **Security: Expression Injection Concerns**

**Problem**: If rules come from untrusted sources, property paths could be malicious.

**Current mitigation**: Limited (PathResolver only accesses properties)

**Recommendation**:
- Whitelist allowed property paths per event type
- Sandbox property access
- Add security policies

```csharp
public class SecurePathResolver : IPathResolver
{
    private readonly HashSet<string> _allowedPaths;

    public object? Resolve(object root, string path)
    {
        if (!_allowedPaths.Contains(path))
            throw new SecurityException($"Access to '{path}' is not allowed");

        // ... resolve
    }
}
```

---

### 14. **Missing: Rule Versioning**

**Problem**: No versioning strategy for rules.

**Scenario**: Rule format changes over time, backward compatibility needed.

**Recommendation**:
```csharp
public class RuleDefinition
{
    public string Version { get; init; } = "1.0";
    // ... rest of properties
}

// In parser
public static RuleDefinition Parse(string json)
{
    var version = DetectVersion(json);
    return version switch
    {
        "1.0" => ParseV1(json),
        "2.0" => ParseV2(json),
        _ => throw new NotSupportedException($"Version {version} not supported")
    };
}
```

---

### 15. **Architecture: Consider CQRS Pattern for Rules**

**Consideration**: Separate read/write models for rules.

**Current**: Single RuleDefinition used for everything.

**Alternative**:
```csharp
// Write model (validation, business logic)
public class CreateRuleCommand { ... }
public class UpdateRuleCommand { ... }

// Read model (optimized for queries)
public class RuleQuery { ... }
public class CompiledRule { ... } // Pre-compiled for execution
```

**Verdict**: Probably overkill for current scope. Consider for v2+.

---

## 🎯 Prioritized Recommendations

### **High Priority (Do Now)**
1. ✅ Add priority sorting to RuleEngine
2. ✅ Add IRuleRepository abstraction
3. ✅ Add operator validation (operand count)
4. ✅ Add structured logging/observability
5. ✅ Add integration tests
6. ✅ Add rule validation on load

### **Medium Priority (Next Sprint)**
7. ⚠️ Separate EvaluationContext concerns
8. ⚠️ Add rule indexing by event type
9. ⚠️ Add security/whitelisting for paths
10. ⚠️ Add fluent rule builder API

### **Low Priority (Future)**
11. 💡 Consider expression support
12. 💡 Add rule versioning
13. 💡 Parallel action execution (optional)
14. 💡 CQRS pattern for rules

---

## 🏗️ Alternative Architecture Patterns

### **Pattern 1: Rule Compiler**
Pre-compile rules to delegates for better performance:
```csharp
public interface IRuleCompiler
{
    Func<EvaluationContext, Task> Compile(RuleDefinition rule);
}
```

**Pros**: Much faster execution
**Cons**: More complex, harder to debug

---

### **Pattern 2: Actor Model**
Use actors (like Akka.NET) for rule execution:
```csharp
public class RuleActor : ReceiveActor
{
    public RuleActor()
    {
        Receive<EvaluationContext>(ctx => { ... });
    }
}
```

**Pros**: Natural fit for event-driven systems
**Cons**: Adds dependency, steeper learning curve

---

### **Pattern 3: Pipeline Pattern**
Chain rule evaluators:
```csharp
public interface IRulePipeline
{
    IRulePipeline Filter(Func<RuleDefinition, bool> predicate);
    IRulePipeline Sort(Func<RuleDefinition, IComparable> keySelector);
    Task ExecuteAsync(EvaluationContext context);
}
```

**Pros**: More flexible, composable
**Cons**: May be over-engineered

---

## 🎓 Final Thoughts

**What's Good**:
- Clean, pragmatic design
- Good separation of concerns
- Extensible architecture
- Performance-conscious

**What Needs Work**:
- Missing infrastructure pieces (repository, validation, logging)
- No observability
- Limited error handling
- Security considerations

**Overall Assessment**: **Solid foundation, needs production hardening.**

The core design is sound. Focus on adding the infrastructure pieces (repository, validation, logging) before adding new features. The architecture is extensible enough to grow with new requirements.

**Biggest Risk**: Lack of error handling and observability will make debugging production issues difficult.

**Recommended Next Steps**:
1. Add IRuleRepository + InMemoryRuleRepository
2. Add comprehensive logging
3. Add rule validation
4. Add integration tests
5. Document performance characteristics and limitations
6. Consider security implications if rules come from untrusted sources
