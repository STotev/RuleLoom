# Copilot Instructions for RuleForge

> A .NET 10.0 library for building event-driven business rule engines with JSON-based rule authoring.

## Build, Test, and Lint

```bash
# Build entire solution
dotnet build

# Build specific project
dotnet build src/RuleForge.Core

# Run all tests
dotnet test

# Run specific test project
dotnet test tests/RuleForge.Tests
dotnet test tests/RuleForge.Tests.Integration

# Run single test
dotnet test --filter "FullyQualifiedName~RuleJsonParserTests.Parse_SimpleRuleWithPathOperand_Success"

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## High-Level Architecture

### Three-Layer Design

1. **RuleForge.Abstractions** - Zero-dependency contracts layer
   - Contains all interfaces and base types
   - MUST have no external dependencies (critical constraint)
   - Defines the extension model for operators and actions

2. **RuleForge.Core** - Default implementation
   - RuleEngine, ConditionEvaluator
   - Built-in operators: `eq`, `changed`, `and`, `or`, `not`
   - Built-in actions: `log`

3. **RuleForge.Extensions.DependencyInjection** - Optional DI helpers

### Core Execution Flow

```
Event (eventType, current, previous)
  → RuleEngine filters rules by eventType
  → Rules sorted by Priority (descending)
  → For each rule:
    → ConditionEvaluator evaluates condition tree
    → If true: Execute actions (each can have optional condition)
```

### Key Design Patterns

**Operator Pattern**: All conditions are operators with operands. Operators are registered by name and can be nested:
- Simple: `{ "operator": "eq", "operands": [{"path": "Price"}, {"value": 100}] }`
- Nested: `{ "operator": "and", "operands": [<operator1>, <operator2>] }`

**Path Resolution**: String-based property access with scope prefixes
- Implicit current: `"Price"` → resolves from `Current` object
- Explicit: `"current.Price"` or `"previous.Price"`
- Uses reflection via PathResolver

**Action Conditions**: Actions can have their own conditions that are evaluated independently after the rule condition passes.

## Key Conventions

### Namespace Structure
- Follow folder structure: `RuleForge.<Project>.<Folder>`
- Example: `RuleForge.Core.Operators`, `RuleForge.Abstractions.Condition`

### Extension Points

**Custom Operators** must implement `IConditionOperator`:
```csharp
public class GreaterThanOperator : IConditionOperator
{
    public string Name => "gt"; // lowercase convention
    public int MinOperands => 2;
    public int MaxOperands => 2; // use int.MaxValue for unlimited

    public bool Evaluate(EvaluationContext ctx,
                        IReadOnlyList<ConditionNode> operands,
                        IConditionEvaluator evaluator)
    {
        var left = evaluator.ResolveValue(ctx, operands[0]);
        var right = evaluator.ResolveValue(ctx, operands[1]);
        // implementation
    }
}
```

**Custom Actions** must implement `IActionExecutor`:
```csharp
public class SendEmailAction : IActionExecutor
{
    public string Type => "send-email"; // kebab-case convention

    public async Task ExecuteAsync(ActionDefinition action, EvaluationContext context)
    {
        var to = action.Parameters["to"]?.ToString();
        // implementation
    }
}
```

### JSON Rule Format

Rules are typically defined in JSON and parsed via `RuleJsonParser`:
- Use `RuleJsonParser.Parse(json)` for single rule
- Use `RuleJsonParser.ParseMany(json)` for arrays
- Parser is case-insensitive for property names

### Testing Patterns

**Unit tests** (RuleForge.Tests):
- Test individual components (operators, parsers)
- Use xUnit framework
- Follow naming: `[MethodName]_[Scenario]_[ExpectedOutcome]`

**Integration tests** (RuleForge.Tests.Integration):
- Test full RuleEngine execution flows
- Include multiple operators and actions
- Test with real data objects

### Important Constraints

- **Abstractions must stay dependency-free** - This is a hard requirement for the library-first design
- Rules execute in Priority order (descending) - Higher priority = executed first
- Failed rule execution continues to next rule (fail-safe by design)
- PathResolver is reflection-based - performance characteristics to consider for high-volume scenarios

### Code Style

- 4 spaces for C# files (2 spaces for JSON/XML)
- File-scoped namespaces
- Expression-bodied members for simple properties
- Use collection expressions `[]` where appropriate
- Prefer explicit over implicit typing for clarity
