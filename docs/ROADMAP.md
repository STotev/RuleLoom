# BusinessRules Library - Development Roadmap

**Last Updated**: March 21, 2026
**Version**: 0.1.0-alpha
**Vision**: A stable, developer-friendly library for building event-driven business rule engines with end-user rule authoring capabilities.

---

## 🎯 Big Picture

### Target Audience
1. **Developers** - Integrate the library, define operators/actions/events/schemas
2. **End Users** - Create and manage rules using developer-defined building blocks
3. **UI Builders** - Future UI library will consume schemas to build rule authoring interfaces

### Core Value Proposition
- Developers define **what's possible** (operators, actions, events, field schemas)
- End users define **business logic** (rules using those building blocks)
- System executes rules when events are triggered
- Works in distributed environments (microservices + event bus)

### Key Scenarios

**Scenario 1: E-Commerce Order Rules**
```csharp
// Developer defines what's available
builder.AddEvent("OrderCreated", schema => schema
    .WithField("Total", FieldType.Number)
    .WithField("CustomerTier", FieldType.String))
  .AddAction<SendEmailAction>()
  .AddAction<ApplyDiscountAction>();

// End user creates rule via UI (saved as JSON)
{
  "name": "VIP Welcome",
  "eventType": "OrderCreated",
  "condition": {
    "operator": "eq",
    "operands": [
      { "path": "CustomerTier" },
      { "value": "VIP" }
    ]
  },
  "actions": [
    { "type": "sendEmail", "parameters": { "template": "vip-welcome" } }
  ]
}
```

**Scenario 2: Distributed Microservices**
```csharp
// Service A: Order Service
await eventPublisher.PublishAsync("OrderCreated", order);

// Service B: Rule Engine Service (receives event, evaluates rules)
public class RuleEngineService : IEventConsumer<OrderCreatedEvent>
{
    public async Task ConsumeAsync(OrderCreatedEvent evt)
    {
        await ruleEngine.ExecuteAsync(evt.EventType, evt.Data, evt.PreviousData);
    }
}
```

---

## 📋 Development Phases

### **Phase 1: Core Foundation** ✅ (Current Priority)
> Stable, production-ready core with minimal dependencies

### **Phase 2: Developer Experience** 🎯 (Next)
> Rich APIs for library integration and configuration

### **Phase 3: User Experience** 📅 (Future)
> End-user rule management and validation

### **Phase 4: Infrastructure & Scale** 🔮 (Later)
> Persistence, observability, distributed execution

---

## Phase 1: Core Foundation (v0.1 - v0.3)

**Goal**: Build rock-solid core abstractions and engine with zero dependencies in abstractions.

### ✅ Already Completed
- [x] Core abstractions (ConditionNode, IConditionOperator, IActionExecutor)
- [x] RuleEngine with basic execution flow
- [x] ConditionEvaluator with operator registry
- [x] PathResolver with compiled expression caching
- [x] Basic operators (eq, changed, and, or, not)
- [x] JSON rule parser with polymorphic support
- [x] Basic LogAction for testing

---

### 🔥 Phase 1.1: Critical Stability Fixes (v0.1) ✅ COMPLETE
> **Completed**: March 21, 2026 | **Priority**: CRITICAL

**Objective**: Fix critical issues before building new features.

#### 1.1.1 Rule Execution Order & Priority ✅
**Problem**: Rules execute in arbitrary order, priority field is ignored.

**Tasks**:
- [x] Add priority-based sorting to RuleEngine.ExecuteAsync
- [x] Document priority behavior (higher = executed first)
- [x] Add unit tests for priority ordering
- [ ] Consider adding StopOnFirstMatch option (future)

**Files to modify**:
- `src/BusinessRules.Core/Engine/RuleEngine.cs`

**Acceptance Criteria**:
```csharp
var rules = new[] {
    new RuleDefinition { Priority = 1, ... },
    new RuleDefinition { Priority = 10, ... },
    new RuleDefinition { Priority = 5, ... }
};
// Execution order must be: Priority 10 → 5 → 1
```

---

#### 1.1.2 Operator Validation ✅
**Problem**: No validation that operators receive correct operand counts.

**Tasks**:
- [x] Add `MinOperands` and `MaxOperands` properties to `IConditionOperator`
- [x] Update all existing operators with operand constraints
- [x] Implement validation in `ConditionEvaluator.Evaluate`
- [x] Add descriptive error messages
- [x] Add unit tests for validation

**Files to modify**:
- `src/BusinessRules.Abstractions/Condition/IConditionOperator.cs`
- `src/BusinessRules.Core/Evaluators/ConditionEvaluator.cs`
- `src/BusinessRules.Core/Operators/*.cs` (all operators)

**Example**:
```csharp
public interface IConditionOperator
{
    string Name { get; }
    int MinOperands { get; }
    int MaxOperands { get; }
    bool Evaluate(EvaluationContext ctx, IReadOnlyList<ConditionNode> operands, IConditionEvaluator evaluator);
}

// In ConditionEvaluator
if (op.Operands.Count < @operator.MinOperands || op.Operands.Count > @operator.MaxOperands)
{
    throw new InvalidOperationException(
        $"Operator '{op.Operator}' requires {operator.MinOperands}-{operator.MaxOperands} operands, got {op.Operands.Count}");
}
```

---

#### 1.1.3 Null Safety & Error Handling ✅
**Problem**: PathResolver crashes on missing properties, no graceful error handling.

**Tasks**:
- [x] Add null checks throughout PathResolver
- [x] Handle missing properties gracefully (return null, don't throw)
- [x] Add try-catch in RuleEngine with continue-on-error strategy
- [x] Document error handling behavior
- [x] Add error handling tests

**Files to modify**:
- `src/BusinessRules.Abstractions/PathResolver.cs`
- `src/BusinessRules.Abstractions/EvaluationContext.cs`
- `src/BusinessRules.Core/Engine/RuleEngine.cs`

**Design Decision**:
- Missing properties return `null` (don't throw)
- Invalid operators throw exception (fail fast)
- Rule evaluation errors logged but don't stop other rules

---

#### 1.1.4 Integration Tests ✅
**Problem**: Only unit tests exist, no end-to-end testing.

**Tasks**:
- [x] Create `BusinessRules.Tests.Integration` project
- [x] Test complete flow: Parse JSON → Execute → Verify
- [x] Test all operators with realistic data
- [x] Test error scenarios
- [x] Add performance benchmarks (optional)

**New files**:
- `tests/BusinessRules.Tests.Integration/BusinessRules.Tests.Integration.csproj`
- `tests/BusinessRules.Tests.Integration/RuleEngineIntegrationTests.cs`

**Results**: 10/11 tests passing (1 skipped for nested properties investigation)

---

**Phase 1.1 Summary**: See [PHASE_1_1_SUMMARY.md](PHASE_1_1_SUMMARY.md) for complete details.

---

### 🎯 Phase 1.2: Schema System (v0.2)
> **ETA**: 2-3 weeks | **Priority**: HIGH

**Objective**: Enable developers to define field schemas for UI metadata and validation.

#### 1.2.1 Field Schema Definition
**Purpose**: Define available fields, types, and metadata for each event.

**New abstractions**:
```csharp
// src/BusinessRules.Abstractions/Schema/FieldSchema.cs
public class FieldSchema
{
    public required string Path { get; init; }           // "User.Email"
    public required FieldType Type { get; init; }        // FieldType.String
    public string? DisplayName { get; init; }            // "User's Email Address"
    public string? Description { get; init; }
    public bool IsRequired { get; init; }
    public Dictionary<string, object>? Metadata { get; init; } // UI hints
}

// src/BusinessRules.Abstractions/Schema/FieldType.cs
public enum FieldType
{
    String,
    Number,
    Boolean,
    Date,
    Object,
    Array
}
```

**Tasks**:
- [ ] Create FieldSchema and FieldType
- [ ] Add validation rules support (min/max, regex, etc.)
- [ ] Add JSON serialization
- [ ] Add unit tests

---

#### 1.2.2 Event Schema Definition
**Purpose**: Link event types to available fields and operators.

**New abstractions**:
```csharp
// src/BusinessRules.Abstractions/Schema/EventSchema.cs
public class EventSchema
{
    public required string EventType { get; init; }
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public List<FieldSchema> Fields { get; init; } = [];
    public List<string> AllowedOperators { get; init; } = []; // Optional filter
}

// src/BusinessRules.Abstractions/Schema/ISchemaProvider.cs
public interface ISchemaProvider
{
    EventSchema? GetSchema(string eventType);
    IReadOnlyList<EventSchema> GetAllSchemas();
}
```

**Tasks**:
- [ ] Create EventSchema and ISchemaProvider
- [ ] Implement InMemorySchemaProvider
- [ ] Add schema registration API
- [ ] Add JSON export for UI consumption

---

#### 1.2.3 Schema-Based Validation
**Purpose**: Validate rules against schemas before execution.

**New abstractions**:
```csharp
// src/BusinessRules.Abstractions/Validation/IRuleValidator.cs
public interface IRuleValidator
{
    ValidationResult Validate(RuleDefinition rule);
}

// src/BusinessRules.Abstractions/Validation/ValidationResult.cs
public class ValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<ValidationError> Errors { get; init; } = [];
}

public class ValidationError
{
    public required string Path { get; init; }
    public required string Message { get; init; }
    public ValidationErrorType Type { get; init; }
}

public enum ValidationErrorType
{
    MissingField,
    InvalidOperator,
    InvalidOperandCount,
    TypeMismatch,
    MissingAction
}
```

**Implementation**:
```csharp
// src/BusinessRules.Core/Validation/SchemaRuleValidator.cs
public class SchemaRuleValidator : IRuleValidator
{
    private readonly ISchemaProvider _schemaProvider;
    private readonly IEnumerable<IConditionOperator> _operators;
    private readonly IEnumerable<IActionExecutor> _actions;

    public ValidationResult Validate(RuleDefinition rule)
    {
        var errors = new List<ValidationError>();
        var schema = _schemaProvider.GetSchema(rule.EventType);

        // Validate event type exists
        // Validate all paths exist in schema
        // Validate operators are registered
        // Validate operand counts
        // Validate actions are registered

        return new ValidationResult { Errors = errors };
    }
}
```

**Tasks**:
- [ ] Create validation abstractions
- [ ] Implement SchemaRuleValidator
- [ ] Add validation to RuleJsonParser (optional)
- [ ] Add comprehensive validation tests

---

### 🚀 Phase 1.3: Event System (v0.3)
> **ETA**: 2-3 weeks | **Priority**: HIGH

**Objective**: Provide easy way to trigger rules from anywhere (in-process or distributed).

#### 1.3.1 Event Publishing Abstraction
**Purpose**: Decouple rule triggering from rule execution.

**New abstractions**:
```csharp
// src/BusinessRules.Abstractions/Events/IRuleEventPublisher.cs
public interface IRuleEventPublisher
{
    Task PublishAsync<TData>(
        string eventType,
        TData current,
        TData? previous = default,
        CancellationToken cancellationToken = default) where TData : class;
}

// src/BusinessRules.Abstractions/Events/RuleEvent.cs
public class RuleEvent
{
    public required string EventType { get; init; }
    public required object Current { get; init; }
    public object? Previous { get; init; }
    public string? CorrelationId { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string? Source { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}
```

**Tasks**:
- [ ] Create IRuleEventPublisher interface
- [ ] Create RuleEvent class
- [ ] Add correlation ID support
- [ ] Add metadata support

---

#### 1.3.2 In-Process Event Publisher
**Purpose**: Simple in-memory event publishing for single-service scenarios.

**Implementation**:
```csharp
// src/BusinessRules.Core/Events/InProcessEventPublisher.cs
public class InProcessEventPublisher : IRuleEventPublisher
{
    private readonly RuleEngine _ruleEngine;
    private readonly IRuleRepository _ruleRepository;

    public async Task PublishAsync<TData>(
        string eventType,
        TData current,
        TData? previous = default,
        CancellationToken cancellationToken = default) where TData : class
    {
        var rules = await _ruleRepository.GetRulesAsync(eventType);
        await _ruleEngine.ExecuteAsync(eventType, current, previous, rules);
    }
}
```

**Tasks**:
- [ ] Implement InProcessEventPublisher
- [ ] Wire to RuleEngine
- [ ] Add cancellation token support
- [ ] Add tests

---

#### 1.3.3 Rule Repository Abstraction
**Purpose**: Abstract rule storage from rule execution.

**New abstractions**:
```csharp
// src/BusinessRules.Abstractions/Repository/IRuleRepository.cs
public interface IRuleRepository
{
    Task<IReadOnlyList<RuleDefinition>> GetRulesAsync(
        string eventType,
        CancellationToken cancellationToken = default);

    Task<RuleDefinition?> GetRuleAsync(
        string ruleId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RuleDefinition>> GetAllRulesAsync(
        CancellationToken cancellationToken = default);
}
```

**Implementation**:
```csharp
// src/BusinessRules.Core/Repository/InMemoryRuleRepository.cs
public class InMemoryRuleRepository : IRuleRepository
{
    private readonly Dictionary<string, RuleDefinition> _rules = new();
    private readonly Dictionary<string, List<RuleDefinition>> _rulesByEventType = new();

    public Task<IReadOnlyList<RuleDefinition>> GetRulesAsync(
        string eventType,
        CancellationToken cancellationToken = default)
    {
        if (_rulesByEventType.TryGetValue(eventType, out var rules))
            return Task.FromResult<IReadOnlyList<RuleDefinition>>(rules);

        return Task.FromResult<IReadOnlyList<RuleDefinition>>(Array.Empty<RuleDefinition>());
    }
}
```

**Tasks**:
- [ ] Create IRuleRepository interface
- [ ] Implement InMemoryRuleRepository with indexing
- [ ] Add CRUD operations (for Phase 3)
- [ ] Add tests

---

#### 1.3.4 Fluent Configuration API
**Purpose**: Make developer setup intuitive and discoverable.

**Example API**:
```csharp
var builder = new RuleEngineBuilder()
    // Register events with schemas
    .AddEvent("PropertyUpdated", schema => schema
        .WithDisplayName("Property Updated")
        .WithField("Price", FieldType.Number, "Property Price")
        .WithField("Status", FieldType.String, "Status"))

    // Register operators
    .AddOperator<EqualsOperator>()
    .AddOperator<ChangedOperator>()
    .AddOperators(typeof(AndOperator).Assembly) // Scan assembly

    // Register actions
    .AddAction<LogAction>()
    .AddAction<SendEmailAction>()

    // Configure repository
    .UseInMemoryRules(rules)

    // Configure validation
    .UseSchemaValidation();

var engine = builder.Build();
var publisher = builder.BuildPublisher();
```

**New files**:
- `src/BusinessRules.Core/Configuration/RuleEngineBuilder.cs`

**Tasks**:
- [ ] Create RuleEngineBuilder
- [ ] Add fluent methods for registration
- [ ] Add schema configuration methods
- [ ] Add tests and examples

---

### 📦 Phase 1.4: Documentation & Packaging (v0.3)
> **ETA**: 1 week | **Priority**: MEDIUM

#### 1.4.1 NuGet Packaging

**Packages to create**:
1. **BusinessRules.Abstractions** - Core contracts, zero dependencies
2. **BusinessRules.Core** - Engine and default implementations
3. **BusinessRules** - Meta-package (includes both)

**Tasks**:
- [ ] Add package metadata (authors, license, icon, tags)
- [ ] Add README.md to each package
- [ ] Add XML documentation comments to all public APIs
- [ ] Configure NuGet package generation
- [ ] Set up versioning strategy (SemVer 2.0)

---

#### 1.4.2 Documentation

**Documents to create**:
- [ ] `docs/getting-started.md` - Quick start guide
- [ ] `docs/concepts.md` - Core concepts explained
- [ ] `docs/operators.md` - All operators documented
- [ ] `docs/schema-system.md` - Schema system guide
- [ ] `docs/event-system.md` - Event publishing guide
- [ ] `docs/examples/` - Code examples for common scenarios
- [ ] `docs/architecture/` - Architecture decision records (ADRs)

---

#### 1.4.3 Comprehensive Samples

**Sample projects**:
- [ ] `samples/BusinessRules.Samples.ECommerce/` - Order processing rules
- [ ] `samples/BusinessRules.Samples.RealEstate/` - Property management rules
- [ ] `samples/BusinessRules.Samples.MultiTenant/` - Multi-tenant scenario

---

## Phase 2: Developer Experience (v0.4 - v0.6)

**Goal**: Make library integration seamless and intuitive.

### Phase 2.1: ASP.NET Core Integration (v0.4)
> **ETA**: 2 weeks

**New package**: `BusinessRules.Extensions.DependencyInjection`

**Example API**:
```csharp
services.AddBusinessRules(options =>
{
    options.AddDefaultOperators();
    options.AddOperator<CustomOperator>();
    options.AddAction<SendEmailAction>();
    options.AddEventSchema("OrderCreated", schema => { ... });
    options.UseInMemoryRules();
});
```

**Tasks**:
- [ ] Create IServiceCollection extensions
- [ ] Auto-register operators and actions
- [ ] Add configuration options
- [ ] Add health checks
- [ ] Add middleware for metrics

---

### Phase 2.2: Rule Builder API (v0.5)
> **ETA**: 2 weeks

**Type-safe, fluent rule construction**:
```csharp
var rule = RuleBuilder
    .For("PropertyUpdated")
    .Named("High Value Alert")
    .WithPriority(10)
    .When(c => c.Path("Price").GreaterThan(1000000))
    .Execute(a => a.SendEmail(...))
    .Build();
```

---

### Phase 2.3: Advanced Operators (v0.6)
> **ETA**: 1-2 weeks

**Operators to add**:
- Comparison: gt, lt, gte, lte, between, in
- String: contains, startsWith, endsWith, matches
- Collection: any, all, count
- Date: after, before, withinDays, isToday

---

## Phase 3: User Experience (v0.7 - v0.9)

**Goal**: Enable end-user rule management.

### Phase 3.1: Rule Management (v0.7)
- [ ] Rule CRUD operations in IRuleRepository
- [ ] Rule versioning and history
- [ ] Rule lifecycle (draft, active, inactive, archived)

### Phase 3.2: Rule Testing (v0.8)
- [ ] Rule simulator for testing against sample data
- [ ] Debug view showing condition evaluation flow

### Phase 3.3: Multi-Tenancy (v0.9)
- [ ] Tenant context for rules
- [ ] Tenant-specific filtering
- [ ] Tenant-specific schemas (optional)

---

## Phase 4: Infrastructure & Scale (v1.0+)

**Goal**: Production-ready infrastructure.

### Phase 4.1: Persistence (v1.0)
- [ ] SQL Server repository (Entity Framework)
- [ ] PostgreSQL support
- [ ] MongoDB repository
- [ ] Redis caching

### Phase 4.2: Distributed Events (v1.1)
- [ ] MassTransit integration
- [ ] RabbitMQ adapter
- [ ] Message correlation and retry

### Phase 4.3: Observability (v1.2)
- [ ] Structured logging (ILogger)
- [ ] OpenTelemetry metrics
- [ ] Distributed tracing

### Phase 4.4: Scheduling (v1.3)
- [ ] Time-based rule evaluation
- [ ] Cron expressions
- [ ] Recurring rules

### Phase 4.5: Management API (v1.4)
- [ ] REST API for rule management
- [ ] Schema retrieval endpoints
- [ ] Rule validation/simulation endpoints
- [ ] Optional dashboard UI

---

## 🎯 Success Criteria

### Phase 1 (v0.3) - "Core Complete"
- ✅ Stable API contracts
- ✅ Schema system working
- ✅ Event publishing working
- ✅ Validation working
- ✅ 90%+ test coverage
- ✅ Documentation complete
- ✅ Zero critical bugs

### Phase 2 (v0.6) - "Developer Ready"
- ✅ ASP.NET Core integration seamless
- ✅ Fluent APIs intuitive
- ✅ Rich operator library

### Phase 3 (v0.9) - "User Ready"
- ✅ Rule management complete
- ✅ Multi-tenant support
- ✅ Rule testing/simulation

### Phase 4 (v1.0+) - "Production Ready"
- ✅ Persistence layers complete
- ✅ Distributed event support
- ✅ Full observability
- ✅ Management API

---

## 📐 Design Principles

Throughout all phases, maintain:

1. **Library-First**: Not a framework, not a service. Integrate anywhere.
2. **Zero Lock-In**: Abstractions in separate package with zero dependencies.
3. **Convention Over Configuration**: Sensible defaults, opt-in complexity.
4. **Type Safety**: Strong typing, compile-time checking where possible.
5. **Performance**: Compiled expressions, caching, lazy loading.
6. **Testability**: Every component mockable and testable.
7. **Extensibility**: Plugin model for operators, actions, persistence.
8. **Documentation**: Every public API documented with examples.

---

## 🚫 Out of Scope (For Now)

- Visual rule builder UI (separate project later)
- Machine learning / AI rule suggestions
- Rule conflict detection (maybe v2.0)
- Workflow engine capabilities
- Business process management

---

## 📅 Timeline

| Phase | Version | Duration | Target |
|-------|---------|----------|--------|
| Phase 1.1 | v0.1 | 1-2 weeks | Apr 2026 |
| Phase 1.2 | v0.2 | 2-3 weeks | May 2026 |
| Phase 1.3 | v0.3 | 2-3 weeks | Jun 2026 |
| Phase 1.4 | v0.3 | 1 week | Jun 2026 |
| Phase 2 | v0.4-0.6 | 5-6 weeks | Aug 2026 |
| Phase 3 | v0.7-0.9 | 4-5 weeks | Sep 2026 |
| Phase 4 | v1.0+ | Ongoing | Oct 2026+ |

**Total to v1.0**: ~6 months

---

## 📝 Notes

This roadmap is a living document. Priorities may shift based on:
- User feedback
- Performance discoveries
- Integration challenges
- New requirements

**Current Focus**: Phase 1.1 - Critical Stability Fixes





