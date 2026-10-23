namespace RuleLoom.Abstractions.Condition;

/// <summary>
/// Messages shared by evaluation (<see cref="OperatorContext"/>) and validation (<see cref="OperandValidationContext"/>),
/// so a problem reads the same whether a rule is validated or run.
/// </summary>
internal static class OperandMessages
{
    public static string ExpectsPath(string operatorName, int index, ConditionNode operand) =>
        $"Operator '{operatorName}' expects a path at operand {index}, but got {operand.GetType().Name}.";
}
