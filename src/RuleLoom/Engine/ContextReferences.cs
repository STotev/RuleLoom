using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Rule;

namespace RuleLoom.Engine;

/// <summary>
/// Finds the contexts (<c>@name</c>) that rules use in paths: rule and action conditions, and schedule reference paths.
/// </summary>
internal static class ContextReferences
{
    public static IReadOnlyCollection<string> Collect(IEnumerable<RuleDefinition> rules)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules)
        {
            AddFrom(rule.Condition, names);

            foreach (var action in rule.Actions)
            {
                AddFrom(action.Condition, names);
                AddPath(action.Schedule?.ReferencePath, names);
            }
        }

        return names;
    }

    private static void AddFrom(ConditionNode? node, HashSet<string> names)
    {
        switch (node)
        {
            case PathOperand operand:
                AddPath(operand.Path, names);
                break;
            case OperatorCondition condition:
                foreach (var operand in condition.Operands)
                    AddFrom(operand, names);
                break;
        }
    }

    private static void AddPath(string? path, HashSet<string> names)
    {
        // Invalid paths fail when the rule resolves them.
        if (path is not null && RulePath.TryParse(path, out var parsed, out _) && parsed.Root == PathRoot.Context)
            names.Add(parsed.Context!);
    }
}
