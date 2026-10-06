using RuleForge.Abstractions;
using RuleForge.Abstractions.Condition;

namespace RuleForge.Evaluators;

public class ConditionEvaluator : IConditionEvaluator
{
    private readonly Dictionary<string, IConditionOperator> _operators = new(StringComparer.OrdinalIgnoreCase);

    public ConditionEvaluator(IEnumerable<IConditionOperator> operators)
    {
        ArgumentNullException.ThrowIfNull(operators);

        foreach (var @operator in operators)
        {
            if (_operators.TryGetValue(@operator.Name, out var existing))
            {
                throw new ArgumentException(
                    existing.Name == @operator.Name
                        ? $"Operator '{@operator.Name}' is registered more than once."
                        : $"Operator '{@operator.Name}' conflicts with '{existing.Name}' (names are case-insensitive).",
                    nameof(operators));
            }

            _operators.Add(@operator.Name, @operator);
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
