using BusinessRules.Abstractions;

namespace BusinessRules.Core.Engine;

public class RuleEngine
{
    private readonly IEnumerable<RuleDefinition> _rules;
    private readonly Dictionary<string, IConditionOperator> _operators;
    private readonly Dictionary<string, IAction> _actions;

    public RuleEngine(
        IEnumerable<RuleDefinition> rules,
        IEnumerable<IConditionOperator> operators,
        IDictionary<string, IAction> actions)
    {
        _rules = rules;
        _operators = operators.ToDictionary(x => x.Name);
        _actions = new Dictionary<string, IAction>(actions);
    }

    public async Task ProcessAsync(EventContext evt)
    {
        var rules = _rules
            .Where(r => r.EventName == evt.EventName)
            .OrderBy(r => r.Priority);

        foreach (var rule in rules)
        {
            if (!Evaluate(rule.Condition, evt))
                continue;

            foreach (var actionDef in rule.Actions)
            {
                if (!Evaluate(actionDef.Condition, evt))
                    continue;

                if (!_actions.TryGetValue(actionDef.Type, out var action))
                    continue;

                await action.ExecuteAsync(new ActionContext
                {
                    Event = evt,
                    Data = actionDef.Data
                });
            }
        }
    }

    private bool Evaluate(ConditionDefinition? condition, EventContext evt)
    {
        if (condition == null) return true;

        if (!_operators.TryGetValue(condition.Operator, out var op))
            return false;

        var ctx = new EvaluationContext
        {
            Current = evt.Current,
            Previous = evt.Previous
        };

        return op.Evaluate(ctx, condition.Args);
    }
}