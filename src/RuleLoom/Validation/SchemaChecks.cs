using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Scheduling;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Abstractions.Action;
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
    // The envelope's fields ($event.), described from RuleEvent like any data type, so they follow its properties. The
    // state has its own roots ($current, $previous). Metadata is a dictionary: its app-defined keys aren't checked.
    private static readonly IReadOnlyList<FieldDefinition> EnvelopeFields = DescribeEnvelope();

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
        else if (parsed.Root == PathRoot.Event)
        {
            if (parsed.Property.Length == 0)
                return null;

            (fields, owner) = (EnvelopeFields, "the event's envelope ($event)");
        }
        else
        {
            if (parsed.Property.Length == 0)
                return null;

            (fields, owner) = (@event.Fields, @event.EventType);
        }

        return Walk(fields, owner, parsed, path, problems);
    }

    /// <summary>
    /// The field <paramref name="parsed"/>'s property names among <paramref name="fields"/> (of <paramref name="owner"/>),
    /// reporting a field that doesn't exist or can't be gone into; <c>null</c> then, or when it can't be checked.
    /// </summary>
    public static FieldDefinition? Walk(IReadOnlyList<FieldDefinition> fields, string owner, RulePath parsed, PathNode path,
        ValidationProblems problems)
    {
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

    private static IReadOnlyList<FieldDefinition> DescribeEnvelope()
    {
        var envelope = new ParametersSchema<RuleEvent>();
        envelope.Parameter(e => e.Current).Ignore();
        envelope.Parameter(e => e.Previous).Ignore();
        return envelope.Build().Fields;
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

/// <summary>
/// A scheduled action is checked and run against its subject as its loader returns it when the action is due
/// (<c>Subject(...).LoadedBy(...)</c>), so the current-state paths it reads (its rule's condition, its own condition and
/// parameters; not its schedule's reference path, read when it's scheduled) must be fields of that subject too.
/// </summary>
/// <param name="subjectFields">The fields of the subject the loader returns.</param>
/// <param name="owner">How messages name it, e.g. <c>the subject loaded by 'order' when the action runs</c>.</param>
/// <param name="eventPaths">The event's paths, resolved first: a path that isn't a field of the event is reported there.</param>
internal sealed class SubjectPathCheck(IReadOnlyList<FieldDefinition> subjectFields, string owner, SchemaPaths eventPaths) : RuleCheck
{
    private bool _ruleSchedules;
    private bool _inActions;
    private bool _readWhenDue;

    public override void Rule(RuleDefinition rule, ValidationProblems problems) =>
        _ruleSchedules = rule.Actions.Any(IsScheduled);

    public override void Action(ActionDefinition action, string location, ValidationProblems problems) =>
        (_inActions, _readWhenDue) = (true, IsScheduled(action));

    // The schedule's reference path, visited after this, is read when the action is scheduled.
    public override void Schedule(ScheduleMetadata schedule, string location, ValidationProblems problems) => _readWhenDue = false;

    public override void Path(PathNode path, ValidationProblems problems)
    {
        if (!(_inActions ? _readWhenDue : _ruleSchedules) ||
            path.Parsed is not { Root: PathRoot.Current, Property.Length: > 0 } parsed ||
            eventPaths.Resolve(path, problems) is null)
        {
            return;
        }

        SchemaPaths.Walk(subjectFields, owner, parsed, path, problems);
    }

    private static bool IsScheduled(ActionDefinition action) => action.Schedule is { Mode: not ScheduleMode.Immediate };
}
