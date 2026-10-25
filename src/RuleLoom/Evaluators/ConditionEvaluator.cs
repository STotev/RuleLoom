using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Condition;

namespace RuleLoom.Evaluators;

/// <summary>
/// Evaluates conditions with the operators of a <see cref="Catalog"/>.
/// </summary>
public class ConditionEvaluator(Catalog catalog) : IConditionEvaluator
{
    private readonly Catalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    public bool Evaluate(EvaluationContext context, ConditionNode node)
    {
        if (node is not OperatorCondition op)
            throw new InvalidOperationException($"Expected an operator condition but got {node.GetType().Name}.");

        if (!_catalog.TryGetOperator(op.Operator, out var @operator))
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
