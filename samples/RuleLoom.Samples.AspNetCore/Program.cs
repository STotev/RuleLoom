using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Quartz;
using RuleLoom.Abstractions;
using RuleLoom.Actions;
using RuleLoom.Engine;
using RuleLoom.Samples.AspNetCore;
using Serilog;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Logs through Serilog: levels and the console format (with timestamps and each log's source) are in appsettings.json.
builder.Services.AddSerilog((services, logging) => logging
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services));

builder.Services
    .AddRuleLoom(rules => rules.AddCatalog(catalog => catalog
        .AddFromAssemblyOf<OrderPlacedSchema>() // the event schemas, SendEmailAction and LoyaltyCheckAction
        .AddContextProvider<CustomerProvider>("customer")
        .AddContextProvider<OrderHistoryProvider>("orderHistory")
        .AddContextProvider<OrderProvider>("order")    // the subject of order events, loaded when a scheduled action is due
        .AddAction<ConsoleLogAction>())         // built in, for development; not registered by default
        .Configure(engine =>
        {
            // A reminder that runs more than a minute late (the scheduler was down) is skipped rather than sent late.
            engine.SkipOverdueActions = true;
            engine.OverdueGracePeriod = TimeSpan.FromMinutes(1);
        }))
    .AddSingleton<CustomerDirectory>()
    .AddSingleton<OrderStore>()
    .AddScoped<TenantRuleStore>()
    .AddSingleton<ActionScheduler>()
    .AddQuartz()
    .AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

// RuleLoom's spans, nested under each request's, and its metrics on the console; a real app uses an exporter such as OTLP.
// builder.Services.AddOpenTelemetry()
//     .WithTracing(tracing => tracing
//         .AddAspNetCoreInstrumentation()
//         .AddSource(RuleLoomDiagnostics.Name)
//         .AddConsoleExporter())
//     .WithMetrics(metrics => metrics
//         .AddMeter(RuleLoomDiagnostics.Name)
//         .AddConsoleExporter());

var app = builder.Build();

// What a rule-authoring UI uses: GET /rules/catalog (the events and their contexts, their fields, the operators and the
// actions with their parameters) and POST /rules/validate. A real app secures them: .RequireAuthorization().
app.MapRuleLoom("/rules");

app.MapPost("/events", async (
    EventRequest request,
    TenantRuleStore ruleStore,
    OrderStore orders,
    RuleEngine engine,
    ActionScheduler scheduler,
    CancellationToken cancellationToken) =>
{
    var rules = await ruleStore.LoadAsync(request.TenantId, cancellationToken);
    if (rules is null)
        return Results.NotFound(new { error = $"Unknown tenant '{request.TenantId}'." });

    // Stands in for the shop's database, which the producer updated before sending the event.
    if (request.EventType is "OrderPlaced" or "OrderUpdated")
        orders.Save(request.TenantId, request.Current.Deserialize<Order>(JsonSerializerOptions.Web)!);

    var @event = new RuleEvent(request.EventType, request.Current, request.Previous)
    {
        EventId = request.EventId,
        Subject = request.EntityId,
        OccurredAt = request.OccurredAt,
        Metadata = new Dictionary<string, object?> { ["tenantId"] = request.TenantId }
    };
    var result = await engine.ExecuteAsync(@event, rules.Valid, cancellationToken);
    var jobs = await scheduler.ScheduleAsync(request.TenantId, @event, result.ScheduledActions, cancellationToken);

    return Results.Ok(EventResponse.From(result, rules.Invalid, jobs));
});

app.Run();
