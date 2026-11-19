using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Contexts;
using RuleLoom.Engine;
using Spectre.Console;

namespace RuleLoom.Samples.Console.Scenarios;

/// <summary>
/// What RuleLoom reports besides its results: OpenTelemetry traces and metrics (an ActivitySource and a Meter named
/// "RuleLoom") and logs (ILogger). An app adds them to its OpenTelemetry setup and exports them (the ASP.NET Core sample
/// sends them to the Aspire Dashboard). Here they're caught in-process with .NET's own listeners, to show exactly what's
/// emitted: names, ids, statuses and durations, never the order's data or parameter values.
/// </summary>
public static class Observability
{
    private const string Rules = """
    [
      {
        "id": "vip-thank-you",
        "name": "VIP thank you",
        "eventType": "OrderPlaced",
        "condition": { "operator": "eq", "operands": [{ "path": "@customer.Tier" }, { "value": "gold" }] },
        "actions": [{ "action": "send-email", "parameters": { "to": { "$path": "@customer.Email" }, "subject": "Thank you" } }]
      },
      {
        "id": "payment-reminder",
        "name": "Payment reminder",
        "eventType": "OrderPlaced",
        "condition": { "operator": "eq", "operands": [{ "path": "Status" }, { "value": "AwaitingPayment" }] },
        "actions": [{
          "action": "send-email",
          "parameters": { "to": { "$path": "Customer.Email" }, "subject": "Your payment is due tomorrow" },
          "schedule": { "mode": "Delay", "amount": 1, "unit": "Days" }
        }]
      },
      {
        "id": "big-order",
        "name": "Big order",
        "eventType": "OrderPlaced",
        "condition": { "operator": "gte", "operands": [{ "path": "Total" }, { "value": 1000 }] },
        "actions": [{ "action": "send-email", "parameters": { "to": "sales@example.com", "subject": "Big order" } }]
      },
      {
        "id": "broken",
        "name": "Broken rule",
        "eventType": "OrderPlaced",
        "condition": { "operator": "gt", "operands": [{ "path": "Total" }] },
        "actions": []
      }
    ]
    """;

    public static async Task RunAsync()
    {
        Show.Scenario(11, "Observability", "One event, four rules (one of them broken): the logs, the trace and the metrics RuleLoom emits.");
        Show.Json("Rules", Rules);

        using var traces = new TraceRecorder();
        using var metrics = new MetricRecorder();

        var catalog = new CatalogBuilder()
            .AddEvent<OrderPlacedWithCustomerSchema>()        // from scenario 3
            .AddContextProvider<CustomerProvider>("customer")
            .AddAction<SendEmailAction>()                     // from scenario 4
            .Build();
        var engine = new RuleEngine(catalog,
            contextProviders: new DirectContextProviderFactory().Add(new CustomerProvider()),
            loggerFactory: new PrintingLoggerFactory());      // an app passes its own (AddRuleLoom does)

        Show.Step("Ada places an order; the logs, as they're written:");
        var order = new Order("SO-1001", OrderStatus.AwaitingPayment, 640.50m, Customers.Ada);
        var placed = new RuleEvent("OrderPlaced", order) { EventId = "evt-42", Subject = order.Number };
        await engine.ExecuteAsync(placed, RuleJsonParser.ParseMany(Rules));
        Show.End();

        Show.Step("The trace: one span for the event, with the context load and each action that ran under it");
        traces.Print();
        Show.End();

        Show.Step("The metrics");
        metrics.Print();
        Show.Note("Rules aren't spans (hundreds of rules per event would be noise): their outcomes are counts on the event's span.");
        Show.Note("The subject and the event id are only on spans and in Debug logs: a subject can itself be personal data.");
        Show.End();
    }

    /// <summary>Records RuleLoom's spans, as an OpenTelemetry exporter would receive them.</summary>
    private sealed class TraceRecorder : IDisposable
    {
        private readonly List<Activity> _spans = [];
        private readonly ActivityListener _listener;

        public TraceRecorder()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == RuleLoomDiagnostics.Name,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = span => { lock (_spans) _spans.Add(span); }
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public void Print()
        {
            var tree = new Tree("[grey]Trace[/]");
            foreach (var root in _spans.Where(s => !_spans.Any(p => p.SpanId == s.ParentSpanId)).OrderBy(s => s.StartTimeUtc))
                Add(tree.AddNode(Describe(root)), root);

            AnsiConsole.Write(tree);
        }

        private void Add(TreeNode node, Activity parent)
        {
            foreach (var (key, value) in parent.TagObjects)
                node.AddNode($"[grey]{Markup.Escape(key)}[/] = {Markup.Escape(value?.ToString() ?? "")}");
            foreach (var spanEvent in parent.Events)
                node.AddNode($"[yellow]event[/] {Markup.Escape(spanEvent.Name)}: {Markup.Escape(string.Join(", ", spanEvent.Tags.Select(t => $"{t.Key} = {t.Value}")))}");
            foreach (var child in _spans.Where(s => s.ParentSpanId == parent.SpanId).OrderBy(s => s.StartTimeUtc))
                Add(node.AddNode(Describe(child)), child);
        }

        private static string Describe(Activity span)
        {
            var status = span.Status == ActivityStatusCode.Error ? $" [red]error: {Markup.Escape(span.StatusDescription ?? "")}[/]" : "";
            return $"[bold]{span.OperationName}[/] [grey]{span.Duration.TotalMilliseconds:0.0} ms[/]{status}";
        }

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>Records RuleLoom's counters and durations, summed by their tags.</summary>
    private sealed class MetricRecorder : IDisposable
    {
        private readonly Dictionary<(string Instrument, string Tags), (int Count, double Sum, string? Unit)> _values = [];
        private readonly MeterListener _listener = new();

        public MetricRecorder()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == RuleLoomDiagnostics.Name)
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
            _listener.Start();
        }

        private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var key = (instrument.Name, string.Join(", ", tags.ToArray().Select(t => $"{t.Key.Replace("ruleloom.", "")}={t.Value}")));
            lock (_values)
            {
                var (count, sum, _) = _values.GetValueOrDefault(key);
                _values[key] = (count + 1, sum + value, instrument.Unit);
            }
        }

        public void Print()
        {
            var table = new Table().AddColumns("Instrument", "Tags", "Value");
            foreach (var ((instrument, tags), (count, sum, unit)) in _values.OrderBy(v => v.Key.Instrument))
            {
                var value = unit == "s" ? $"{count} × {sum / count * 1000:0.0} ms" : sum.ToString("0");
                table.AddRow(instrument, Markup.Escape(tags), value);
            }

            AnsiConsole.Write(table);
        }

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>Prints each log line as it's written, with its level and category.</summary>
    private sealed class PrintingLoggerFactory : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => new PrintingLogger(categoryName);

        public void AddProvider(ILoggerProvider provider) { }

        public void Dispose() { }

        private sealed class PrintingLogger(string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var level = logLevel switch
                {
                    LogLevel.Debug => "[grey]DBG[/]",
                    LogLevel.Information => "[blue]INF[/]",
                    LogLevel.Warning => "[yellow]WRN[/]",
                    _ => "[red]ERR[/]"
                };
                AnsiConsole.MarkupLine($"  {level} [grey]{Markup.Escape(category)}:[/] {Markup.Escape(formatter(state, exception))}");
            }
        }
    }
}
