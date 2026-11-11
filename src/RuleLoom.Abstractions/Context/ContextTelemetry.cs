using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace RuleLoom.Abstractions.Context;

/// <summary>Traces and times context loads (<c>ruleloom.context</c> spans, <c>ruleloom.context.duration</c>).</summary>
internal static class ContextTelemetry
{
    private static readonly string? Version = typeof(ContextTelemetry).Assembly.GetName().Version?.ToString();
    private static readonly ActivitySource Source = new(RuleLoomDiagnostics.Name, Version);
    private static readonly Meter Meter = new(RuleLoomDiagnostics.Name, Version);

    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "ruleloom.context.duration", "s", "How long loading a context took, by provider and outcome.");

    public static async Task<object?> LoadAsync(string context, string provider, Func<Task<object?>> load)
    {
        using var activity = Source.StartActivity("ruleloom.context");
        activity?.SetTag("ruleloom.context", context);
        activity?.SetTag("ruleloom.provider", provider);
        var started = Stopwatch.GetTimestamp();
        var outcome = "failure";

        try
        {
            var loaded = await load().ConfigureAwait(false);
            outcome = "success";
            return loaded;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            throw;
        }
        finally
        {
            Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,
                new KeyValuePair<string, object?>("ruleloom.provider", provider), new KeyValuePair<string, object?>("ruleloom.outcome", outcome));
        }
    }
}
