namespace RuleLoom.Abstractions;

/// <summary>
/// The name RuleLoom's traces and metrics use: add it to OpenTelemetry with <c>AddSource(RuleLoomDiagnostics.Name)</c>
/// and <c>AddMeter(RuleLoomDiagnostics.Name)</c>. Spans and metrics carry names and ids from the catalog and the rules,
/// statuses and durations, never event state, context data or parameter values.
/// </summary>
public static class RuleLoomDiagnostics
{
    /// <summary>The <c>ActivitySource</c> and <c>Meter</c> name: <c>RuleLoom</c>.</summary>
    public const string Name = "RuleLoom";
}
