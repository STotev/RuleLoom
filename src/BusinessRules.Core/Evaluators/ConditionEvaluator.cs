using BusinessRules.Abstractions;
using BusinessRules.Abstractions.Condition;

namespace BusinessRules.Core.Evaluators;

public class ConditionEvaluator : IConditionEvaluator
{
    private readonly IDictionary<string, IConditionOperator> _operators;

    public ConditionEvaluator(IEnumerable<IConditionOperator> operators)
    {
        _operators = operators.ToDictionary(x => x.Name);
    }

    public bool Evaluate(EvaluationContext context, ConditionNode node)
    {
        if (node is OperatorCondition op)
        {
            var @operator = _operators[op.Operator];
            return @operator.Evaluate(context, op.Operands, this);
        }

        throw new InvalidOperationException("Cannot evaluate non-boolean node directly");
    }

    public object? ResolveValue(EvaluationContext context, ConditionNode node)
    {
        return node switch
        {
            ValueOperand v => v.Value,
            PathOperand c => context.Resolve(c.Path),
            OperatorCondition op => Evaluate(context, op), // allows nested boolean-as-value
            _ => throw new NotSupportedException()
        };
    }
}
