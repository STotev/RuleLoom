using RuleLoom.Abstractions;
using RuleLoom.Schemas;

namespace RuleLoom.Validation;

/// <summary>
/// Resolves a rule's paths on its event's schema and the schemas of the contexts it uses, once per path: problems are
/// reported the first time a path is resolved.
/// </summary>
/// <param name="catalog">The catalog, which finds the event's contexts and their providers' schemas.</param>
/// <param name="event">The schema of the rule's event.</param>
internal sealed class SchemaPaths(Catalog catalog, EventSchemaDefinition @event)
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

            if (!catalog.TryGetContext(@event.EventType, parsed.Context!, out var found, out var error))
                return Report(problems, path, error);

            if (found.Provider.Schema is not { } context)
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
            if (FieldDefinition.Find(fields, segments[index]) is not { } field)
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
}

/// <summary>Paths must exist in the event's and contexts' schemas.</summary>
internal sealed class SchemaPathCheck(SchemaPaths paths) : RuleCheck
{
    public override void Path(PathNode path, ValidationProblems problems) => paths.Resolve(path, problems);
}
