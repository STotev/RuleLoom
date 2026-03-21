# BusinessRules Quick Start Guide

**Version**: v0.1.0-alpha
**Last Updated**: March 21, 2026

---

## 📦 Installation

```bash
# Clone the repository
git clone https://github.com/yourusername/BusinessRules.git

# Build the solution
cd BusinessRules
dotnet build

# Run tests
dotnet test
```

---

## 🚀 Quick Start

### 1. Create a Simple Rule

```csharp
using BusinessRules.Abstractions.Rule;
using BusinessRules.Abstractions.Condition;
using BusinessRules.Abstractions.Action;

var rule = new RuleDefinition
{
    Name = "High Price Alert",
    EventType = "PropertyUpdated",
    Priority = 10,
    Condition = new OperatorCondition
    {
        Operator = "eq",
        Operands =
        [
            new PathOperand { Path = "Price" },
            new ValueOperand { Value = 100000 }
        ]
    },
    Actions =
    [
        new ActionDefinition
        {
            Type = "log",
            Parameters = { ["message"] = "High price detected!" }
        }
    ]
};
```

### 2. Create Rule Engine

```csharp
using BusinessRules.Core.Engine;
using BusinessRules.Core.Evaluators;
using BusinessRules.Core.Operators;
using BusinessRules.Core.Actions;

var operators = new List<IConditionOperator>
{
    new EqualsOperator(),
    new ChangedOperator(),
    new AndOperator(),
    new OrOperator(),
    new NotOperator()
};

var actions = new List<IActionExecutor>
{
    new LogAction()
};

var engine = new RuleEngine(
    new ConditionEvaluator(operators),
    actions
);
```

### 3. Execute Rules

```csharp
var currentData = new { Price = 100000, Status = "Active" };
var previousData = new { Price = 90000, Status = "Active" };

await engine.ExecuteAsync(
    eventType: "PropertyUpdated",
    current: currentData,
    previous: previousData,
    rules: new[] { rule }
);
```

---

## 📝 Defining Rules in JSON

### Basic Structure

```json
{
  "name": "Rule Name",
  "eventType": "EventName",
  "priority": 10,
  "condition": { /* condition tree */ },
  "actions": [ /* action list */ ]
}
```

### Example: Price Change Alert

```json
{
  "name": "Price Change Alert",
  "eventType": "PropertyUpdated",
  "priority": 5,
  "condition": {
    "operator": "changed",
    "operands": [
      { "path": "Price" }
    ]
  },
  "actions": [
    {
      "type": "log",
      "parameters": {
        "message": "Price changed!"
      }
    }
  ]
}
```

### Parse JSON Rule

```csharp
using BusinessRules.Abstractions.Rule;

var json = File.ReadAllText("rule.json");
var rule = RuleJsonParser.Parse(json);

// Or parse multiple rules
var rulesJson = File.ReadAllText("rules.json");
var rules = RuleJsonParser.ParseMany(rulesJson);
```

---

## 🔧 Built-in Operators

### Comparison Operators

#### `eq` - Equals
Checks if two values are equal.

```json
{
  "operator": "eq",
  "operands": [
    { "path": "Status" },
    { "value": "Active" }
  ]
}
```

**Operands**: Exactly 2

#### `changed` - Value Changed
Checks if a value changed between previous and current state.

```json
{
  "operator": "changed",
  "operands": [
    { "path": "Price" }
  ]
}
```

**Operands**: Exactly 1 (must be a PathOperand)

### Logical Operators

#### `and` - All Conditions True
All operands must evaluate to true.

```json
{
  "operator": "and",
  "operands": [
    { "operator": "eq", "operands": [{ "path": "Status" }, { "value": "Active" }] },
    { "operator": "eq", "operands": [{ "path": "Price" }, { "value": 100000 }] }
  ]
}
```

**Operands**: 1 or more

#### `or` - Any Condition True
At least one operand must evaluate to true.

```json
{
  "operator": "or",
  "operands": [
    { "operator": "eq", "operands": [{ "path": "Status" }, { "value": "Active" }] },
    { "operator": "eq", "operands": [{ "path": "Status" }, { "value": "Pending" }] }
  ]
}
```

**Operands**: 1 or more

#### `not` - Invert Condition
Inverts the result of the operand.

```json
{
  "operator": "not",
  "operands": [
    { "operator": "eq", "operands": [{ "path": "Status" }, { "value": "Inactive" }] }
  ]
}
```

**Operands**: Exactly 1

---

## 🎯 Condition Operands

### PathOperand
References a property on the current or previous object.

```json
{ "path": "PropertyName" }
{ "path": "User.Email" }
{ "path": "current.Price" }
{ "path": "previous.Status" }
```

### ValueOperand
A literal value (string, number, boolean, null, array, object).

```json
{ "value": "Active" }
{ "value": 100000 }
{ "value": true }
{ "value": null }
{ "value": [1, 2, 3] }
```

### OperatorCondition
A nested condition using an operator.

```json
{
  "operator": "eq",
  "operands": [...]
}
```

---

## 🚀 Actions

### LogAction

Logs a message to the console.

```json
{
  "type": "log",
  "parameters": {
    "message": "Your message here"
  }
}
```

### Conditional Actions

Actions can have their own conditions:

```json
{
  "type": "log",
  "parameters": {
    "message": "Only if status is error"
  },
  "condition": {
    "operator": "eq",
    "operands": [
      { "path": "Status" },
      { "value": "Error" }
    ]
  }
}
```

---

## 🔌 Creating Custom Operators

```csharp
using BusinessRules.Abstractions;
using BusinessRules.Abstractions.Condition;

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
        {
            return leftComparable.CompareTo(right) > 0;
        }

        return false;
    }
}
```

**Register the operator**:

```csharp
var operators = new List<IConditionOperator>
{
    new EqualsOperator(),
    new GreaterThanOperator()  // Your custom operator
};
```

---

## 🎬 Creating Custom Actions

```csharp
using BusinessRules.Abstractions;
using BusinessRules.Abstractions.Action;

public class SendEmailAction : IActionExecutor
{
    public string Type => "send-email";

    public async Task ExecuteAsync(ActionDefinition action, EvaluationContext context)
    {
        var to = action.Parameters["to"]?.ToString();
        var subject = action.Parameters["subject"]?.ToString();
        var body = action.Parameters["body"]?.ToString();

        // Your email sending logic here
        await SendEmailAsync(to, subject, body);
    }
}
```

**Register the action**:

```csharp
var actions = new List<IActionExecutor>
{
    new LogAction(),
    new SendEmailAction()  // Your custom action
};
```

**Use in JSON**:

```json
{
  "type": "send-email",
  "parameters": {
    "to": "admin@example.com",
    "subject": "Alert",
    "body": "Price changed!"
  }
}
```

---

## 📊 Priority System

Rules with higher priority values execute first:

```json
[
  { "name": "High Priority", "priority": 10 },  // Executes first
  { "name": "Medium Priority", "priority": 5 },  // Executes second
  { "name": "Low Priority", "priority": 1 }      // Executes last
]
```

Default priority is `0` if not specified.

---

## ⚠️ Error Handling

### Missing Properties

Missing properties return `null` instead of throwing:

```csharp
var data = new { Name = "John" };
var value = context.Resolve("NonExistent");  // Returns null, doesn't throw
```

### Rule Execution Errors

If a rule fails, other rules continue to execute:

```csharp
// Rule 1 fails, but Rules 2 and 3 still execute
await engine.ExecuteAsync(eventType, data, rules);
```

### Operator Validation

Invalid operand counts throw descriptive errors:

```csharp
// This will throw: "Operator 'eq' requires at least 2 operand(s), but got 1"
var condition = new OperatorCondition
{
    Operator = "eq",
    Operands = [new PathOperand { Path = "Price" }]  // Only 1 operand!
};
```

---

## 🧪 Testing Your Rules

```csharp
using Xunit;

public class MyRuleTests
{
    [Fact]
    public async Task Rule_WhenPriceChanges_LogsMessage()
    {
        // Arrange
        var messages = new List<string>();
        var testAction = new TestLogAction(messages);

        var engine = new RuleEngine(
            new ConditionEvaluator(operators),
            new[] { testAction }
        );

        var rule = RuleJsonParser.Parse(ruleJson);

        // Act
        await engine.ExecuteAsync("Test", newData, oldData, new[] { rule });

        // Assert
        Assert.Single(messages);
        Assert.Equal("Expected message", messages[0]);
    }
}
```

---

## 📚 Additional Resources

- [Roadmap](ROADMAP.md) - Development roadmap
- [Phase 1.1 Summary](PHASE_1_1_SUMMARY.md) - Latest changes
- [JSON Parser Documentation](JSON_RULE_PARSER.md) - Detailed JSON format guide

---

## 🆘 Common Issues

### Issue: Rule not executing

**Check**:
1. Does `eventType` match exactly?
2. Is the condition evaluating to `true`?
3. Are all required properties present?
4. Is the operator registered?

### Issue: "Operator not found" error

**Solution**: Register the operator in your operator list:

```csharp
var operators = new List<IConditionOperator>
{
    new YourOperator()  // Add your operator here
};
```

### Issue: "Action type not found" error

**Solution**: Register the action in your action list:

```csharp
var actions = new List<IActionExecutor>
{
    new YourAction()  // Add your action here
};
```

---

## 💡 Best Practices

1. **Use descriptive rule names**: Makes debugging easier
2. **Set appropriate priorities**: Important rules should have higher priority
3. **Keep conditions simple**: Complex conditions are harder to debug
4. **Test your rules**: Write unit tests for custom operators and actions
5. **Validate JSON rules**: Use schema validation before parsing
6. **Handle missing data**: Don't assume all properties exist

---

## 🎉 Next Steps

1. Try creating your first rule
2. Experiment with different operators
3. Create custom operators for your domain
4. Create custom actions for your needs
5. Read the [Roadmap](ROADMAP.md) for upcoming features

**Happy rule building!** 🚀

