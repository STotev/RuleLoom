using System.Collections;
using System.Globalization;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Values;
using RuleLoom.Operators;
using RuleLoom.Schemas;

namespace RuleLoom.Validation;

/// <summary>
/// Resolves a rule's paths on its event's schema and the schemas of the contexts it uses, once per path: problems are
/// reported the first time a path is resolved.
/// </summary>
/// <param name="event">The schema of the rule's event.</param>
/// <param name="contexts">The event's contexts the rule uses, by context name (ignoring case): their schema, or
/// <c>null</c> when the provider describes none. Declared contexts without a registered provider are missing.</param>
internal sealed class SchemaPaths(EventSchemaDefinition @event, IReadOnlyDictionary<string, ContextSchemaDefinition?> contexts)
{
    private readonly Dictionary<PathNode, FieldDefinition?> _resolved = new(ReferenceEqualityComparer.Instance);

    /// <summary>The field a path points at, or <c>null</c> when it's wrong, invalid or can't be checked.</summary>
    public FieldDefinition? Resolve(PathNode path, ValidationProblems problems)
    {
        if (!_resolved.TryGetValue(path, out var field))
            _resolved[path] = field = path.Parsed is { } parsed ? Resolve(parsed, path, problems) : null;

        return field;
    }

    private FieldDefinition? Resolve(RulePath parsed, PathNode path, ValidationProblems problems)
    {
        IReadOnlyList<FieldDefinition> fields;
        string owner;

        if (parsed.Root == PathRoot.Context)
        {
            var name = $"@{parsed.Context}";

            if (!contexts.TryGetValue(parsed.Context!, out var context))
            {
                var declared = @event.Contexts.FirstOrDefault(c => string.Equals(c.Name, parsed.Context, StringComparison.OrdinalIgnoreCase));
                return Report(problems, path, declared is null
                    ? $"'{name}' is not a context of {@event.EventType}."
                    : $"No context provider is registered under '{declared.Provider}' (context '{name}').");
            }

            if (context is null)
            {
                problems.Warning(path.Location, $"Context '{name}' has no schema; paths into it aren't checked.");
                return null;
            }

            if (parsed.Property.Length == 0)
                return null;

            if (context.IsMany)
                return Report(problems, path, $"'{name}' is a collection; paths can't go into its items ('{path.Text}').");

            (fields, owner) = (context.Fields, $"'{name}'");
        }
        else
        {
            if (parsed.Property.Length == 0)
                return null;

            (fields, owner) = (@event.Fields, @event.EventType);
        }

        var segments = parsed.Segments;
        for (var index = 0; ; index++)
        {
            if (Find(fields, segments[index]) is not { } field)
                return Report(problems, path, $"'{segments[index]}' is not a field of {owner}.");

            if (index == segments.Count - 1)
                return field;

            var prefix = $"'{string.Join('.', segments.Take(index + 1))}'";
            switch (field.Type)
            {
                case FieldType.Object when field.Fields.Count > 0:
                    (fields, owner) = (field.Fields, prefix);
                    break;
                case FieldType.Object or FieldType.Any:
                    return null;   // untyped, a dictionary or a repeated type: no declared fields to check
                case FieldType.Array:
                    return Report(problems, path, $"{prefix} is a list; paths can't go into its items ('{path.Text}').");
                default:
                    return Report(problems, path, $"{prefix} is {FieldTypes.Describe(field.Type)} and has no fields ('{path.Text}').");
            }
        }
    }

    private static FieldDefinition? Report(ValidationProblems problems, PathNode path, string message)
    {
        problems.Error(path.Location, message);
        return null;
    }

    /// <summary>Like property paths: an exact-case match wins; an ambiguous case-insensitive match is unknown.</summary>
    private static FieldDefinition? Find(IReadOnlyList<FieldDefinition> fields, string name)
    {
        if (fields.FirstOrDefault(f => f.Name == name) is { } exact)
            return exact;

        var matches = fields.Where(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
}

/// <summary>Paths must exist in the event's and contexts' schemas.</summary>
internal sealed class SchemaPathCheck(SchemaPaths paths) : RuleCheck
{
    public override void Path(PathNode path, ValidationProblems problems) => paths.Resolve(path, problems);
}

/// <summary>How field types read in messages.</summary>
internal static class FieldTypes
{
    public static string Describe(FieldType type) => type switch
    {
        FieldType.Integer or FieldType.Number => "a number",
        FieldType.String => "text",
        FieldType.Boolean => "a boolean",
        FieldType.DateTime => "a date and time",
        FieldType.Date => "a date",
        FieldType.Enum => "an enum",
        FieldType.Object => "an object",
        FieldType.Array => "a list",
        _ => "any value"
    };
}
