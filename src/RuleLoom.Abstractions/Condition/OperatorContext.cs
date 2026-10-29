using RuleLoom.Abstractions.Values;

namespace RuleLoom.Abstractions.Condition;

/// <summary>
/// Everything an <see cref="IConditionOperator"/> needs to evaluate one occurrence of itself: its operands,
/// the current/previous state, and the shared comparison rules.
/// </summary>
/// <remarks>
/// Operand counts are validated against <see cref="IConditionOperator.MinOperands"/> and
/// <see cref="IConditionOperator.MaxOperands"/> before <see cref="IConditionOperator.Evaluate"/> is called.
/// </remarks>
public sealed class OperatorContext
{
    private readonly OperatorCondition _condition;
    private readonly IConditionEvaluator? _evaluator;

    // The paths resolved so far and their values, to name the path of an invalid date-time.
    private List<(string Path, object? Value)>? _resolved;

    /// <summary>
    /// Creates a context for evaluating <paramref name="condition"/>. The evaluator creates these; construct one
    /// directly to unit-test an operator.
    /// </summary>
    /// <param name="condition">The operator condition being evaluated (its name and operands).</param>
    /// <param name="evaluationContext">The current and previous state.</param>
    /// <param name="evaluator">
    /// Evaluates nested operator operands. Only needed when an operand is itself an operator; without it,
    /// <see cref="Test"/> and <see cref="GetValue"/> throw for such operands.
    /// </param>
    public OperatorContext(
        OperatorCondition condition,
        EvaluationContext evaluationContext,
        IConditionEvaluator? evaluator = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(evaluationContext);

        _condition = condition;
        _evaluator = evaluator;
        EvaluationContext = evaluationContext;
    }

    /// <summary>
    /// The name of the operator being evaluated, as written in the rule.
    /// </summary>
    public string OperatorName => _condition.Operator;

    /// <summary>
    /// The number of operands.
    /// </summary>
    public int OperandCount => _condition.Operands.Count;

    /// <summary>
    /// The current and previous state the rule is evaluated against.
    /// </summary>
    public EvaluationContext EvaluationContext { get; }

    /// <summary>
    /// Gets an operand as a value: a value operand's value, a path operand's resolved value
    /// (<c>null</c> if missing), or a nested operator's boolean result.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the operands.</exception>
    public object? GetValue(int index) => GetOperand(index) switch
    {
        ValueOperand value => value.Value,
        PathOperand path => Remember(path.Path, EvaluationContext.Resolve(path.Path)),
        OperatorCondition nested => EvaluateNested(nested, index),
        var other => throw UnsupportedOperand(other, index)
    };

    /// <summary>
    /// Evaluates an operand as a condition. A nested operator is evaluated only when this is called, so
    /// composite operators can short-circuit. A path or value operand counts as a condition when its value is a
    /// <see cref="bool"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the operands.</exception>
    /// <exception cref="InvalidOperationException">The operand's value is not a <see cref="bool"/>.</exception>
    public bool Test(int index) => GetOperand(index) switch
    {
        OperatorCondition nested => EvaluateNested(nested, index),
        PathOperand path => AsCondition(EvaluationContext.Resolve(path.Path), index, $"path '{path.Path}' resolved to"),
        ValueOperand value => AsCondition(value.Value, index, "the value is"),
        var other => throw UnsupportedOperand(other, index)
    };

    /// <summary>
    /// Gets the path string of a path operand, for operators that work with paths rather than values
    /// (such as <c>changed</c>).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the operands.</exception>
    /// <exception cref="InvalidOperationException">The operand is not a path.</exception>
    public string GetPath(int index) => GetOperand(index) is PathOperand path
        ? path.Path
        : throw new InvalidOperationException(OperandMessages.ExpectsPath(OperatorName, index, GetOperand(index)));

    /// <summary>
    /// Resolves any path against the current/previous state, as a path operand would.
    /// </summary>
    public object? Resolve(string path) => Remember(path, EvaluationContext.Resolve(path));

    /// <summary>
    /// Resolves a parsed path against the current/previous state or a context (see <see cref="RulePath"/>).
    /// </summary>
    public object? Resolve(RulePath path) => Remember(path.ToString(), EvaluationContext.Resolve(path));

    /// <summary>
    /// Compares two values for equality with the shared comparison rules (<see cref="ValueComparer.AreEqual"/>), reading
    /// date-times with the evaluation's <see cref="Values.TemporalRules"/>.
    /// </summary>
    /// <exception cref="InvalidTemporalValueException">A value is a wall-clock time that doesn't exist in its zone; the
    /// exception names the path it was resolved from.</exception>
    public bool AreEqual(object? left, object? right)
    {
        try
        {
            return ValueComparer.AreEqual(left, right, EvaluationContext.TemporalRules);
        }
        catch (InvalidTemporalValueException ex) when (PathOf(ex) is { } path)
        {
            throw ex.At(path);
        }
    }

    /// <summary>
    /// Orders two values with the shared comparison rules (<see cref="ValueComparer.TryCompare"/>), reading date-times
    /// with the evaluation's <see cref="Values.TemporalRules"/>.
    /// </summary>
    /// <returns><c>false</c> if the values can't be ordered.</returns>
    /// <exception cref="InvalidTemporalValueException">A value is a wall-clock time that doesn't exist in its zone; the
    /// exception names the path it was resolved from.</exception>
    public bool TryCompare(object? left, object? right, out int result)
    {
        try
        {
            return ValueComparer.TryCompare(left, right, EvaluationContext.TemporalRules, out result);
        }
        catch (InvalidTemporalValueException ex) when (PathOf(ex) is { } path)
        {
            throw ex.At(path);
        }
    }

    private object? Remember(string path, object? value)
    {
        (_resolved ??= []).Add((path, value));
        return value;
    }

    private string? PathOf(InvalidTemporalValueException ex) =>
        ex.Path is null ? _resolved?.FirstOrDefault(r => r.Value is not null && TemporalRules.Format(r.Value) == ex.Value).Path : null;

    private ConditionNode GetOperand(int index)
    {
        if ((uint)index >= (uint)OperandCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index), index, $"Operator '{OperatorName}' has {OperandCount} operand(s).");
        }

        return _condition.Operands[index];
    }

    private bool EvaluateNested(OperatorCondition nested, int index)
    {
        if (_evaluator is null)
        {
            throw new InvalidOperationException(
                $"Operand {index} of operator '{OperatorName}' is a nested operator ('{nested.Operator}'), " +
                "which needs an IConditionEvaluator; pass one to the OperatorContext constructor.");
        }

        return _evaluator.Evaluate(EvaluationContext, nested);
    }

    private bool AsCondition(object? value, int index, string description) => value is bool flag
        ? flag
        : throw new InvalidOperationException(
            $"Operator '{OperatorName}' expects a condition at operand {index}, but {description} " +
            $"{Describe(value)} instead of a boolean.");

    private InvalidOperationException UnsupportedOperand(ConditionNode node, int index) =>
        new($"Operator '{OperatorName}' can't use operand {index} of type {node.GetType().Name}.");

    private static string Describe(object? value) => value is null ? "null" : value.GetType().Name;
}
