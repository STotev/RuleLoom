using RuleLoom.Abstractions.Context;

namespace RuleLoom.Tests;

/// <summary>
/// Context provider that returns a fixed value (or throws) and records its calls.
/// </summary>
internal sealed class FakeContextProvider(Func<ContextRequest, object?> load) : IContextProvider
{
    private int _calls;

    public FakeContextProvider(object? value) : this(_ => value)
    {
    }

    public int Calls => _calls;

    public List<ContextRequest> Requests { get; } = [];

    /// <summary>
    /// When set, each load waits for this task, so tests can check that loads overlap.
    /// </summary>
    public Task? Gate { get; init; }

    public async ValueTask<object?> GetAsync(ContextRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        lock (Requests)
            Requests.Add(request);

        if (Gate is not null)
            await Gate.WaitAsync(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        return load(request);
    }

    public static FakeContextProvider Failing(string message) =>
        new(_ => throw new InvalidOperationException(message));
}
