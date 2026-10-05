using RuleForge.Abstractions;
using RuleForge.Abstractions.Condition;

namespace RuleForge.Evaluators;

public class ConditionEvaluator : IConditionEvaluator
{
    private readonly Dictionary<string, IConditionOperator> _operators = [];

    public ConditionEvaluator(IEnumerable<IConditionOperator> operators)
    {
        ArgumentNullException.ThrowIfNull(operators);

        foreach (var @operator in operators)
        {
            if (!_operators.TryAdd(@operator.Name, @operator))
                throw new ArgumentException($"Operator '{@operator.Name}' is registered more than once.", nameof(operators));
        }
    }

    public bool Evaluate(EvaluationContext context, ConditionNode node)
    {
        if (node is not OperatorCondition op)
            throw new InvalidOperationException($"Expected an operator condition but got {node.GetType().Name}.");

        if (!_operators.TryGetValue(op.Operator, out var @operator))
            throw new InvalidOperationException($"Operator '{op.Operator}' not found");

        // Validate operand count
        if (op.Operands.Count < @operator.MinOperands)
        {
            throw new InvalidOperationException(
                $"Operator '{op.Operator}' requires at least {@operator.MinOperands} operand(s), but got {op.Operands.Count}");
        }

        if (op.Operands.Count > @operator.MaxOperands)
        {
            throw new InvalidOperationException(
                $"Operator '{op.Operator}' allows at most {@operator.MaxOperands} operand(s), but got {op.Operands.Count}");
        }

        return @operator.Evaluate(new OperatorContext(op, context, this));
    }
}
