using BusinessRules.Abstractions;
using BusinessRules.Abstractions.Condition;

namespace BusinessRules.Core.Evaluators;

public class ExpressionEvaluator : IExpressionEvaluator
{
    private readonly Dictionary<string, IOperator> _operators;

    public ExpressionEvaluator(IEnumerable<IOperator> operators)
    {
        _operators = operators.ToDictionary(x => x.Name);
    }

    public object? Evaluate(EvaluationContext context, ConditionNode node)
    {
        return node switch
        {
            ValueOperand v => v.Value,
            PathOperand c => context.Resolve(c.Path),
            OperatorCondition op => _operators[op.Operator].Evaluate(context, op.Operands, this),
            _ => throw new NotSupportedException()
        };
    }
}
