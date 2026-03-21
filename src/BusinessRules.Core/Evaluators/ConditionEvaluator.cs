using BusinessRules.Abstractions;
using BusinessRules.Abstractions.Condition;

namespace BusinessRules.Core.Evaluators;

public class ConditionEvaluator : IConditionEvaluator
{
    private readonly Dictionary<string, IConditionOperator> _operators;

    public ConditionEvaluator(IEnumerable<IConditionOperator> operators)
    {
        _operators = operators.ToDictionary(x => x.Name);
    }

    public bool Evaluate(EvaluationContext context, ConditionNode node)
    {
        if (node is not OperatorCondition op)
            throw new InvalidOperationException("Root must be operator");

        if (!_operators.TryGetValue(op.Operator, out var @operator))
            throw new InvalidOperationException($"Operator '{op.Operator}' not found");

        return @operator.Evaluate(context, op.Operands, this);
    }

    public object? ResolveValue(EvaluationContext context, ConditionNode node)
    {
        return node switch
        {
            ValueOperand v => v.Value,
            PathOperand p => context.Resolve(p.Path),
            OperatorCondition op => Evaluate(context, op),
            _ => throw new NotSupportedException()
        };
    }
}
