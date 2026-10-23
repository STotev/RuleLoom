using RuleLoom.Schemas;

namespace RuleLoom.Abstractions.Condition;

/// <summary>
/// What <see cref="IConditionOperator.ValidateOperands"/> gets when a rule is validated: the operator's operands, their
/// parsed paths and, when validating against schemas, the fields those paths point at. Report problems with
/// <see cref="Error(int, string)"/> and <see cref="Warning(int, string)"/>; the validator adds their locations in the rule.
/// </summary>
public sealed class OperandValidationContext
{
    private readonly OperatorCondition _condition;
    private readonly Func<int, FieldDefinition?>? _fields;
    private readonly List<OperandProblem> _problems = [];

    /// <summary>
    /// Creates the context for <paramref name="condition"/>.
    /// </summary>
    /// <param name="condition">The operator node being validated.</param>
    /// <param name="fields">The schema field of the path operand at an index, when validating against schemas.</param>
    public OperandValidationContext(OperatorCondition condition, Func<int, FieldDefinition?>? fields = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        (_condition, _fields) = (condition, fields);
    }

    /// <summary>The operator's name as written in the rule.</summary>
    public string OperatorName => _condition.Operator;

    /// <summary>The number of operands (within the operator's limits).</summary>
    public int OperandCount => _condition.Operands.Count;

    /// <summary>The problems reported so far.</summary>
    public IReadOnlyList<OperandProblem> Problems => _problems;

    /// <summary>The operand at <paramref name="index"/>.</summary>
    public ConditionNode GetOperand(int index) => _condition.Operands[index];

    /// <summary>The parsed path of the operand at <paramref name="index"/>; <c>null</c> when it isn't a path or the path
    /// isn't valid (the validator reports invalid paths itself).</summary>
    public RulePath? GetPath(int index) =>
        GetOperand(index) is PathOperand operand && RulePath.TryParse(operand.Path, out var path, out _) ? path : null;

    /// <summary>
    /// The parsed path of the operand at <paramref name="index"/>, reporting an error when it isn't a path (the same
    /// message <see cref="OperatorContext.GetPath"/> throws when the rule runs). <c>null</c> when it isn't a path or the
    /// path isn't valid (the validator reports invalid paths itself).
    /// </summary>
    public RulePath? RequirePath(int index)
    {
        if (GetOperand(index) is PathOperand)
            return GetPath(index);

        Error(index, OperandMessages.ExpectsPath(OperatorName, index, GetOperand(index)));
        return null;
    }

    /// <summary>The schema field the path operand at <paramref name="index"/> points at; <c>null</c> without schemas,
    /// for other operands, and for paths that can't be checked.</summary>
    public FieldDefinition? GetField(int index) => _fields?.Invoke(CheckIndex(index));

    /// <summary>Reports an error at the operand at <paramref name="index"/>.</summary>
    public void Error(int index, string message) => _problems.Add(new(CheckIndex(index), message, IsWarning: false));

    /// <summary>Reports an error at the operator.</summary>
    public void Error(string message) => _problems.Add(new(null, message, IsWarning: false));

    /// <summary>Reports a warning (the rule stays valid) at the operand at <paramref name="index"/>.</summary>
    public void Warning(int index, string message) => _problems.Add(new(CheckIndex(index), message, IsWarning: true));

    /// <summary>Reports a warning (the rule stays valid) at the operator.</summary>
    public void Warning(string message) => _problems.Add(new(null, message, IsWarning: true));

    private int CheckIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, OperandCount);
        return index;
    }
}

/// <summary>
/// A problem an operator found in its operands.
/// </summary>
/// <param name="Operand">The operand's index, or <c>null</c> for the operator as a whole.</param>
/// <param name="Message">What is wrong.</param>
/// <param name="IsWarning">Whether it's a warning (the rule stays valid) rather than an error.</param>
public sealed record OperandProblem(int? Operand, string Message, bool IsWarning);
