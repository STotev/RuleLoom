using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Rule;

namespace RuleLoom.Validation;

/// <summary>
/// Walks a rule once, in document order, and hands each part to every check: the rule, its condition, the actions with
/// their conditions, and every path (condition operands and schedule reference paths).
/// </summary>
internal sealed class RuleWalker(IReadOnlyDictionary<string, IConditionOperator> operators, IReadOnlyList<RuleCheck> checks)
{
    public RuleValidationResult Walk(RuleDefinition rule)
    {
        var problems = new ValidationProblems();

        foreach (var check in checks)
            check.Rule(rule, problems);

        Root(rule.Condition, "$.condition", "The rule's root condition", problems);

        for (var index = 0; index < rule.Actions.Count; index++)
        {
            var action = rule.Actions[index];
            var location = $"$.actions[{index}]";

            foreach (var check in checks)
                check.Action(action, location, problems);

            if (action.Condition is not null)
                Root(action.Condition, $"{location}.condition", "An action's condition", problems);

            if (action.Schedule is not { } schedule)
                continue;

            foreach (var check in checks)
                check.Schedule(schedule, location, problems);

            if (schedule.ReferencePath is { } referencePath)
                Path(referencePath, $"{location}.schedule.referencePath", parent: null, problems);
        }

        return problems.ToResult();
    }

    private void Root(ConditionNode? condition, string location, string description, ValidationProblems problems)
    {
        foreach (var check in checks)
            check.RootCondition(condition, location, description, problems);

        if (condition is OperatorCondition root)
            Operator(root, location, problems);
    }

    private void Operator(OperatorCondition condition, string location, ValidationProblems problems)
    {
        var @operator = operators.GetValueOrDefault(condition.Operator);

        foreach (var check in checks)
            check.Operator(condition, @operator, location, problems);

        var paths = new PathNode?[condition.Operands.Count];

        for (var index = 0; index < condition.Operands.Count; index++)
        {
            var operandLocation = $"{location}.operands[{index}]";

            switch (condition.Operands[index])
            {
                case OperatorCondition nested:
                    Operator(nested, operandLocation, problems);
                    break;
                case PathOperand operand:
                    paths[index] = Path(operand.Path, $"{operandLocation}.path", @operator, problems);
                    break;
            }
        }

        foreach (var check in checks)
            check.Operands(condition, @operator, paths, location, problems);
    }

    private PathNode Path(string text, string location, IConditionOperator? parent, ValidationProblems problems)
    {
        RulePath.TryParse(text, out var parsed, out var error);
        var node = new PathNode(text, parsed, error, location, parent);

        foreach (var check in checks)
            check.Path(node, problems);

        return node;
    }
}
