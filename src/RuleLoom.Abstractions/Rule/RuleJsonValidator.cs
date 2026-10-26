using System.Text.Json;

namespace RuleLoom.Abstractions.Rule;

/// <summary>
/// Validates the shape of a rule's JSON before it is deserialized, so errors can name their exact location.
/// Semantic checks (registered operators, operand counts) are not done here.
/// </summary>
internal static class RuleJsonValidator
{
    /// <summary>The key that marks a parameter as a path: <c>{ "$path": "@customer.Email" }</c>.</summary>
    public const string PathKey = "$path";

    private static readonly string[] NodeKinds = ["operator", "path", "value"];

    public static void ValidateRule(JsonElement rule, string path)
    {
        if (rule.ValueKind != JsonValueKind.Object)
            throw new RuleParseException("A rule must be a JSON object.", path);

        RequireNonEmptyString(rule, "id", path);
        RequireNonEmptyString(rule, "name", path);
        RequireNonEmptyString(rule, "eventType", path);

        if (!TryGetProperty(rule, "condition", out var condition))
            throw new RuleParseException("'condition' is required.", $"{path}.condition");

        ValidateCondition(condition, $"{path}.condition", "The rule's root condition");

        if (TryGetProperty(rule, "actions", out var actions))
            ValidateActions(actions, $"{path}.actions");
    }

    private static void ValidateActions(JsonElement actions, string path)
    {
        if (actions.ValueKind != JsonValueKind.Array)
            throw new RuleParseException("'actions' must be an array.", path);

        var index = 0;
        foreach (var action in actions.EnumerateArray())
        {
            var actionPath = $"{path}[{index++}]";

            if (action.ValueKind != JsonValueKind.Object)
                throw new RuleParseException("An action must be a JSON object.", actionPath);

            RequireNonEmptyString(action, "action", actionPath);

            if (TryGetProperty(action, "schedule", out var schedule) &&
                schedule.ValueKind == JsonValueKind.Object &&
                TryGetProperty(schedule, "referencePath", out var referencePath) &&
                referencePath.ValueKind == JsonValueKind.String)
            {
                ValidatePathVariables(referencePath.GetString()!, $"{actionPath}.schedule.referencePath");
            }

            if (TryGetProperty(action, "parameters", out var parameters))
                ValidateParameters(parameters, $"{actionPath}.parameters");

            if (TryGetProperty(action, "condition", out var condition) && condition.ValueKind != JsonValueKind.Null)
                ValidateCondition(condition, $"{actionPath}.condition", "An action's condition");
        }
    }

    private static void ValidateParameters(JsonElement parameters, string path)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
            throw new RuleParseException("'parameters' must be an object.", path);

        foreach (var parameter in parameters.EnumerateObject())
        {
            var parameterPath = $"{path}.{parameter.Name}";

            if (!IsPath(parameter.Value, out var marker))
            {
                RejectNestedPaths(parameter.Value, parameterPath);
                continue;
            }

            if (parameter.Value.EnumerateObject().Count() > 1)
            {
                throw new RuleParseException(
                    $"'{PathKey}' must be the only key of a path, e.g. {{\"{PathKey}\": \"@customer.Email\"}}.", parameterPath);
            }

            if (marker.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(marker.GetString()))
                throw new RuleParseException($"'{PathKey}' must be a non-empty string.", $"{parameterPath}.{PathKey}");

            ValidatePathVariables(marker.GetString()!, $"{parameterPath}.{PathKey}");
        }
    }

    /// <summary>
    /// Whether a parameter value is a path: an object with a <c>$path</c> key (ignoring case, like rule property names).
    /// </summary>
    public static bool IsPath(JsonElement value, out JsonElement path)
    {
        path = default;
        return value.ValueKind == JsonValueKind.Object && TryGetProperty(value, PathKey, out path);
    }

    // Paths can't sit inside a parameter's value (lists of paths aren't supported), so they aren't read as text.
    private static void RejectNestedPaths(JsonElement value, string path)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                if (IsPath(value, out _))
                    throw new RuleParseException($"A path must be a parameter's whole value; '{PathKey}' can't be inside a value.", path);
                foreach (var property in value.EnumerateObject())
                    RejectNestedPaths(property.Value, $"{path}.{property.Name}");
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in value.EnumerateArray())
                    RejectNestedPaths(item, $"{path}[{index++}]");
                break;
        }
    }

    /// <param name="node">The condition node to check.</param>
    /// <param name="path">The JSONPath of <paramref name="node"/>, used in errors.</param>
    /// <param name="operatorRequiredFor">
    /// When set, the node must be an operator; the text names what requires it (e.g. "The rule's root condition").
    /// </param>
    private static void ValidateCondition(JsonElement node, string path, string? operatorRequiredFor = null)
    {
        if (node.ValueKind != JsonValueKind.Object)
            throw new RuleParseException("A condition must be a JSON object.", path);

        var kinds = NodeKinds.Where(kind => TryGetProperty(node, kind, out _)).ToArray();
        if (kinds.Length != 1)
        {
            throw new RuleParseException(
                "A condition must have exactly one of 'operator', 'path' or 'value'" +
                (kinds.Length == 0 ? "." : $", but has {string.Join(", ", kinds.Select(k => $"'{k}'"))}."),
                path);
        }

        if (operatorRequiredFor is not null && kinds[0] != "operator")
        {
            throw new RuleParseException(
                $"{operatorRequiredFor} must be an operator, e.g. {{\"operator\": \"eq\", \"operands\": [...]}}, not a '{kinds[0]}'.",
                path);
        }

        switch (kinds[0])
        {
            case "operator":
                RequireNonEmptyString(node, "operator", path);
                ValidateOperands(node, path);
                break;
            case "path":
                RequireNonEmptyString(node, "path", path);
                ValidatePathVariables(GetString(node, "path"), $"{path}.path");
                break;
        }
    }

    private static void ValidateOperands(JsonElement node, string path)
    {
        if (!TryGetProperty(node, "operands", out var operands))
            return;

        if (operands.ValueKind != JsonValueKind.Array)
            throw new RuleParseException("'operands' must be an array.", $"{path}.operands");

        var index = 0;
        foreach (var operand in operands.EnumerateArray())
            ValidateCondition(operand, $"{path}.operands[{index++}]");
    }

    /// <summary>
    /// Checks the path's syntax (<see cref="RulePath"/>): known variables, context names, reserved prefixes.
    /// </summary>
    private static void ValidatePathVariables(string value, string path)
    {
        if (!RulePath.TryParse(value, out _, out var error))
            throw new RuleParseException(error, path);
    }

    private static string GetString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static void RequireNonEmptyString(JsonElement element, string name, string path)
    {
        if (!TryGetProperty(element, name, out var value))
            throw new RuleParseException($"'{name}' is required.", $"{path}.{name}");

        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new RuleParseException($"'{name}' must be a non-empty string.", $"{path}.{name}");
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
