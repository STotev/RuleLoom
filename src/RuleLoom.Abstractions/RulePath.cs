using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace RuleLoom.Abstractions;

/// <summary>
/// Where a rule path starts reading.
/// </summary>
public enum PathRoot
{
    /// <summary>The subject's current state: <c>Price</c> or <c>$current.Price</c>.</summary>
    Current,

    /// <summary>The subject's previous state: <c>$previous.Price</c>.</summary>
    Previous,

    /// <summary>A context: <c>@customer.Tier</c>.</summary>
    Context
}

/// <summary>
/// A parsed rule path: where it starts (<see cref="Root"/>, and <see cref="Context"/> for context paths) and the property
/// it reads from there. The grammar: <c>Address.City</c> reads the current state; <c>$current.</c> and
/// <c>$previous.</c> (ignoring case) pick a state, alone they mean the whole state; <c>@name.</c> reads a context
/// (contexts have no state, so not after <c>$current</c>/<c>$previous</c>); other <c>$</c> names are reserved, and
/// <c>#</c> is reserved for functions. Parse a <see cref="Condition.PathOperand"/>'s path with <see cref="Parse"/>.
/// </summary>
public sealed class RulePath : IEquatable<RulePath>
{
    // Valid paths are parsed once. Paths can come from rules submitted at run time, so the cache starts over when it
    // reaches about MaxCached entries (the count is approximate under concurrency).
    private const int MaxCached = 10_000;
    private static readonly ConcurrentDictionary<string, RulePath> Cache = new();
    private static int _cachedCount;

    private IReadOnlyList<string>? _segments;
    private RulePath? _otherState;

    private RulePath(PathRoot root, string? context, string property) => (Root, Context, Property) = (root, context, property);

    /// <summary>Where the path starts reading.</summary>
    public PathRoot Root { get; }

    /// <summary>The context name (without <c>@</c>) when <see cref="Root"/> is <see cref="PathRoot.Context"/>.</summary>
    public string? Context { get; }

    /// <summary>The dotted property path read from the root (e.g. <c>Customer.Tier</c>); empty for the whole state or
    /// context.</summary>
    public string Property { get; }

    /// <summary>The property's segments; empty for the whole state or context.</summary>
    public IReadOnlyList<string> Segments => _segments ??= Property.Length == 0 ? [] : Array.AsReadOnly(Property.Split('.'));

    /// <summary>
    /// The same property in the current or previous state, e.g. to compare both (<c>changed</c>).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="state"/> isn't <see cref="PathRoot.Current"/> or
    /// <see cref="PathRoot.Previous"/>.</exception>
    /// <exception cref="InvalidOperationException">This is a context path: contexts have no state.</exception>
    public RulePath InState(PathRoot state)
    {
        if (state is not (PathRoot.Current or PathRoot.Previous))
            throw new ArgumentException("A state is PathRoot.Current or PathRoot.Previous.", nameof(state));

        if (Root == PathRoot.Context)
            throw new InvalidOperationException($"Context '@{Context}' has no current or previous state.");

        return Root == state ? this : _otherState ??= new RulePath(state, null, Property);
    }

    /// <summary>Parses <paramref name="path"/>.</summary>
    /// <exception cref="FormatException">The path isn't valid; the message says why.</exception>
    public static RulePath Parse(string path) =>
        TryParse(path, out var result, out var error) ? result : throw new FormatException(error);

    /// <summary>Parses <paramref name="path"/>, or returns why it isn't valid.</summary>
    public static bool TryParse(string path, [NotNullWhen(true)] out RulePath? result, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (Cache.TryGetValue(path, out result))
        {
            error = null;
            return true;
        }

        (result, error) = Read(path);

        if (result is not null)
        {
            if (Interlocked.Increment(ref _cachedCount) > MaxCached)
            {
                Cache.Clear();
                Interlocked.Exchange(ref _cachedCount, 1);
            }

            Cache.TryAdd(path, result);
        }

        return result is not null;
    }

    /// <summary>Paths are equal when they read the same property from the same root.</summary>
    public bool Equals(RulePath? other) =>
        other is not null && (Root, Context, Property) == (other.Root, other.Context, other.Property);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as RulePath);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Root, Context, Property);

    /// <summary>The path in its canonical form, e.g. <c>Price</c>, <c>$previous.Price</c> or <c>@customer.Tier</c>.</summary>
    public override string ToString()
    {
        var root = Root switch
        {
            PathRoot.Context => $"@{Context}",
            PathRoot.Previous => "$previous",
            _ => Property.Length == 0 ? "$current" : null
        };

        return root is null ? Property : Property.Length == 0 ? root : $"{root}.{Property}";
    }

    private static (RulePath? Path, string? Error) Read(string path)
    {
        var dot = path.IndexOf('.');
        var (first, rest) = dot < 0 ? (path, string.Empty) : (path[..dot], path[(dot + 1)..]);

        if (first.StartsWith('#'))
            return (null, $"Paths starting with '#' are reserved for functions: '{path}'.");

        if (first.StartsWith('@'))
        {
            return first.Length == 1
                ? (null, $"A context path needs a name after '@', e.g. '@customer.Tier': '{path}'.")
                : (new RulePath(PathRoot.Context, first[1..], rest), null);
        }

        if (!first.StartsWith('$'))
            return (new RulePath(PathRoot.Current, null, path), null);

        PathRoot? root = first.Equals("$current", StringComparison.OrdinalIgnoreCase) ? PathRoot.Current
            : first.Equals("$previous", StringComparison.OrdinalIgnoreCase) ? PathRoot.Previous
            : null;

        if (root is null)
            return (null, $"Unknown variable '{first}' in path '{path}'. Use $current or $previous.");

        return rest.StartsWith('@')
            ? (null, $"Contexts have no current or previous state; use '{rest}' instead of '{path}'.")
            : (new RulePath(root.Value, null, rest), null);
    }
}
