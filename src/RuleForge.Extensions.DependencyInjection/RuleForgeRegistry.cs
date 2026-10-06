using Microsoft.Extensions.DependencyInjection;
using RuleForge.Abstractions.Condition;
using RuleForge.Operators;

namespace RuleForge.Extensions.DependencyInjection;

/// <summary>
/// What <see cref="RuleForgeBuilder"/> calls have registered, shared by every <c>AddRuleForge</c> call on one service
/// collection.
/// </summary>
internal sealed class RuleForgeRegistry
{
    private readonly Dictionary<string, Type> _actions = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<OperatorChange> _operatorChanges = [];

    public IReadOnlyCollection<string> ActionNames => _actions.Keys;

    public void AddAction(string name, Type actionType)
    {
        if (_actions.Keys.FirstOrDefault(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            if (existing == name && _actions[existing] == actionType)
                return;

            throw new ArgumentException(
                existing == name
                    ? $"Action name '{name}' is already registered for {_actions[existing].Name}; can't register {actionType.Name} under it."
                    : $"Action name '{name}' conflicts with '{existing}' (names are case-insensitive).",
                nameof(name));
        }

        _actions.Add(name, actionType);
    }

    public bool TryGetActionType(string name, out Type? actionType) => _actions.TryGetValue(name, out actionType);

    public void AddOperator(Type operatorType)
    {
        if (_operatorChanges.Any(change => change.Kind == OperatorChangeKind.Add && change.Type == operatorType))
            return;

        _operatorChanges.Add(new OperatorChange(OperatorChangeKind.Add, operatorType, null));
    }

    public void RemoveOperator(string name) =>
        _operatorChanges.Add(new OperatorChange(OperatorChangeKind.Remove, null, name));

    public void RemoveBuiltInOperators() =>
        _operatorChanges.Add(new OperatorChange(OperatorChangeKind.RemoveBuiltIns, null, null));

    /// <summary>
    /// The built-in operators with every add and remove applied in call order.
    /// </summary>
    public IReadOnlyList<IConditionOperator> BuildOperators(IServiceProvider services)
    {
        var builtIns = BuiltInOperators.Create();
        var operators = builtIns.ToList();

        foreach (var change in _operatorChanges)
        {
            switch (change.Kind)
            {
                case OperatorChangeKind.Add:
                    operators.Add((IConditionOperator)ActivatorUtilities.CreateInstance(services, change.Type!));
                    break;
                case OperatorChangeKind.Remove:
                    operators.RemoveAll(op => string.Equals(op.Name, change.Name, StringComparison.OrdinalIgnoreCase));
                    break;
                case OperatorChangeKind.RemoveBuiltIns:
                    operators.RemoveAll(op => builtIns.Contains(op));
                    break;
            }
        }

        return operators;
    }

    private enum OperatorChangeKind
    {
        Add,
        Remove,
        RemoveBuiltIns
    }

    private sealed record OperatorChange(OperatorChangeKind Kind, Type? Type, string? Name);
}

/// <summary>
/// The singleton set of operators built from the registry, shared by the evaluator and the validator.
/// </summary>
internal sealed class RuleForgeOperators(IReadOnlyList<IConditionOperator> operators)
{
    public IReadOnlyList<IConditionOperator> All { get; } = operators;
}
