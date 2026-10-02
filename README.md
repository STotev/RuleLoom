# RuleForge

> A powerful, extensible .NET library for building event-driven business rule engines with end-user rule authoring capabilities.

[![CI](https://github.com/STotev/RuleForge/actions/workflows/ci.yml/badge.svg)](https://github.com/STotev/RuleForge/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/download)

---

## 🎯 Overview

RuleForge is a library-first approach to building configurable rule engines. It allows developers to define **what's possible** (operators, actions, events) and end users to define **business logic** (rules) without code changes.

### Key Features

- ✅ **Event-Driven**: Trigger rules based on events with current/previous state tracking
- ✅ **JSON-Based Rules**: Define rules externally, no code deployment needed
- ✅ **Type-Safe**: Strong typing with compile-time checks
- ✅ **Extensible**: Easy plugin model for operators and actions
- ✅ **Performance**: Compiled expressions and efficient JSON parsing
- ✅ **Zero Dependencies**: Abstractions layer has no external dependencies
- ✅ **Testable**: Comprehensive test coverage (96%+)

---

## 🚀 Quick Start

### Installation

```bash
# Clone the repository
git clone https://github.com/STotev/RuleForge.git

# Build the solution
cd RuleForge
dotnet build

# Run tests
dotnet test
```

### Basic Example

```csharp
using RuleForge.Abstractions.Action;
using RuleForge.Abstractions.Condition;
using RuleForge.Abstractions.Rule;
using RuleForge.Actions;
using RuleForge.Engine;
using RuleForge.Evaluators;
using RuleForge.Operators;

// Define a rule
var rule = new RuleDefinition
{
    Name = "Price Change Alert",
    EventType = "PropertyUpdated",
    Priority = 10,
    Condition = new OperatorCondition
    {
        Operator = "changed",
        Operands = [new PathOperand { Path = "Price" }]
    },
    Actions =
    [
        new ActionDefinition
        {
            Type = "log",
            Parameters = { ["message"] = "Price changed!" }
        }
    ]
};

// Create rule engine
var engine = new RuleEngine(
    new ConditionEvaluator([new ChangedOperator(), new EqualsOperator()]),
    new DirectActionExecutorFactory([new LogAction()])
);

// Execute rules
var previousData = new { Price = 100000 };
var currentData = new { Price = 150000 };

IReadOnlyList<RuleExecutionResult> results =
    await engine.ExecuteAsync("PropertyUpdated", currentData, previousData, [rule]);
// Output: "Price changed!"
// results[0].ActionResults[0].Status == ActionExecutionStatus.ExecutedImmediately
```

### JSON Example

```json
{
  "name": "High Value Property",
  "eventType": "PropertyUpdated",
  "priority": 5,
  "condition": {
    "operator": "and",
    "operands": [
      {
        "operator": "changed",
        "operands": [{ "path": "Price" }]
      },
      {
        "operator": "eq",
        "operands": [
          { "path": "Status" },
          { "value": "Active" }
        ]
      }
    ]
  },
  "actions": [
    {
      "type": "log",
      "parameters": {
        "message": "High value property updated!"
      }
    }
  ]
}
```

```csharp
var json = File.ReadAllText("rule.json");
var rule = RuleJsonParser.Parse(json);
await engine.ExecuteAsync("PropertyUpdated", currentData, previousData, [rule]);
```

---

## 📚 Documentation

- **[Quick Start Guide](docs/guides/quick-start.md)** - Get started (being refreshed for 0.2.0)
- **[JSON Rule Format](docs/guides/json-rules.md)** - JSON format reference
- **[Roadmap](docs/roadmap.md)** - What's done, what's next, and the backlog
- **[Changelog](CHANGELOG.md)** - Release notes

---

## 🏗️ Architecture

```
┌─────────────────────────────────────────┐
│   RuleForge.Abstractions                │ ← Zero dependencies
│   - Interfaces & Contracts              │
│   - RuleDefinition, ConditionNode       │
└─────────────────────────────────────────┘
                    ↑
┌─────────────────────────────────────────┐
│   RuleForge                             │
│   - RuleEngine                          │
│   - ConditionEvaluator                  │
│   - Built-in Operators & Actions        │
│   - ScheduleCalculator                  │
└─────────────────────────────────────────┘
```

### Design Principles

1. **Library-First** - Not a framework, integrates anywhere
2. **Zero Lock-In** - Abstractions in separate package with no dependencies
3. **Type Safety** - Strong typing throughout
4. **Performance** - Compiled expressions, caching, minimal allocations
5. **Testability** - Every component is mockable

---

## 🔧 Built-in Operators

| Operator | Description | Operands | Example |
|----------|-------------|----------|---------|
| `eq` | Equals | 2 | `Price == 100000` |
| `changed` | Value changed | 1 | `Price changed` |
| `and` | All true | 1+ | `A && B && C` |
| `or` | Any true | 1+ | `A \|\| B \|\| C` |
| `not` | Invert | 1 | `!(Status == "Inactive")` |

More operators are on the [roadmap](docs/roadmap.md).

---

## 🎬 Built-in Actions

| Action | Description | Parameters |
|--------|-------------|------------|
| `log` | Console logging | `message` |

Create custom actions by implementing `IActionExecutor`!

---

## 🔌 Extensibility

### Custom Operator

```csharp
public class GreaterThanOperator : IConditionOperator
{
    public string Name => "gt";
    public int MinOperands => 2;
    public int MaxOperands => 2;

    public bool Evaluate(
        EvaluationContext ctx,
        IReadOnlyList<ConditionNode> operands,
        IConditionEvaluator evaluator)
    {
        var left = evaluator.ResolveValue(ctx, operands[0]);
        var right = evaluator.ResolveValue(ctx, operands[1]);

        if (left is IComparable leftComparable && right != null)
            return leftComparable.CompareTo(right) > 0;

        return false;
    }
}
```

### Custom Action

```csharp
public class SendEmailAction : IActionExecutor
{
    public string Type => "send-email";

    public async Task<ActionResult> ExecuteAsync(
        ActionDefinition action,
        EvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        var to = action.Parameters["to"]?.ToString();
        var subject = action.Parameters["subject"]?.ToString();

        // Your email logic here
        await SendEmailAsync(to, subject, cancellationToken);
        return ActionResult.Success();
    }
}
```

---

## 🧪 Testing

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test tests/RuleForge.Tests
dotnet test tests/RuleForge.Tests.Integration

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

---

## 📦 Project Structure

```
RuleForge/
├── src/
│   ├── RuleForge.Abstractions/     # Core contracts (zero dependencies)
│   ├── RuleForge/                  # Engine implementation
│   └── RuleForge.Extensions.DependencyInjection/ # DI registration (planned)
├── tests/
│   ├── RuleForge.Tests/            # Unit tests
│   └── RuleForge.Tests.Integration/ # Integration tests
├── samples/
│   └── RuleForge.Samples.Console/  # Example usage
└── docs/                           # Guides, roadmap and design specs
```

---

## 🗺️ Roadmap

- ✅ **Done**: core engine and operators, stability fixes, action results, scheduling model,
  action factory, schedule calculation (0.2.0)
- 🎯 **Next**: dependency injection extensions ([#5](https://github.com/STotev/RuleForge/issues/5)),
  then integration tests, samples and migration guide ([#6](https://github.com/STotev/RuleForge/issues/6))
- 📋 **Backlog**: operator API redesign, schema system, event system, NuGet packaging, more operators,
  rule management, persistence, observability

See the [full roadmap](docs/roadmap.md) for details.

---

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

### Development Setup

1. Clone the repository
2. Install .NET 10.0 SDK
3. Run `dotnet build` to build
4. Run `dotnet test` to verify

### Guidelines

- Follow existing code style
- Add tests for new features
- Update documentation
- Keep abstractions dependency-free

---

## 📋 Requirements

- .NET 10.0 or later
- C# 14

---

## 🎓 Use Cases

### E-Commerce
```json
{
  "name": "VIP Customer Welcome",
  "eventType": "OrderCreated",
  "condition": {
    "operator": "eq",
    "operands": [{ "path": "CustomerTier" }, { "value": "VIP" }]
  },
  "actions": [
    { "type": "send-email", "parameters": { "template": "vip-welcome" } },
    { "type": "apply-discount", "parameters": { "percentage": 10 } }
  ]
}
```

### Real Estate
```json
{
  "name": "High Value Property Alert",
  "eventType": "PropertyUpdated",
  "condition": {
    "operator": "and",
    "operands": [
      { "operator": "changed", "operands": [{ "path": "Price" }] },
      { "operator": "gt", "operands": [{ "path": "Price" }, { "value": 1000000 }] }
    ]
  },
  "actions": [
    { "type": "notify-manager", "parameters": { "urgency": "high" } }
  ]
}
```

### Workflow Automation
```json
{
  "name": "Approval Required",
  "eventType": "DocumentUpdated",
  "condition": {
    "operator": "and",
    "operands": [
      { "operator": "eq", "operands": [{ "path": "Status" }, { "value": "Draft" }] },
      { "operator": "gt", "operands": [{ "path": "Amount" }, { "value": 10000 }] }
    ]
  },
  "actions": [
    { "type": "create-approval-task", "parameters": { "approver": "manager" } }
  ]
}
```

---

## ⚠️ Current Limitations

- Nested property resolution needs investigation
- Scheduled actions are calculated (`ScheduledFor`) but not dispatched — bring your own scheduler
- No observability/logging yet
- No persistence layer yet
- Limited operator set

---

## 🤝 Contributing

Contributions are welcome! Please read [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on
reporting bugs, requesting features, and submitting pull requests.

---

## 📄 License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.

---

## 🙏 Acknowledgments

- Built with .NET 10.0
- Uses System.Text.Json for high-performance parsing
- Inspired by rule engines like Drools, Easy Rules, and RulesEngine

---

## 📞 Support

- 📖 [Documentation](docs/)
- 🐛 [Issue Tracker](https://github.com/STotev/RuleForge/issues)
- 💬 [Discussions](https://github.com/STotev/RuleForge/discussions)

---

**Built with ❤️ for the .NET community**
