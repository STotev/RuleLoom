using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Quartz;
using RuleLoom.Abstractions;
using RuleLoom.Samples.AspNetCore.Customers;
using RuleLoom.Samples.AspNetCore.Demo;
using RuleLoom.Samples.AspNetCore.Orders;
using RuleLoom.Samples.AspNetCore.Rules;
using RuleLoom.Samples.AspNetCore.Scheduling;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Logs through Serilog; the levels are in appsettings.json. writeToProviders passes them on to OpenTelemetry too, so
// ASP.NET Core's own console logger goes first, or every line would print twice.
builder.Logging.ClearProviders();
builder.Services.AddSerilog((services, logging) => logging
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services), writeToProviders: true);

// RuleLoom: the catalog (what rules can use) and how rules run.
builder.Services.AddRuleLoom(ruleLoom => ruleLoom
    .AddCatalog(catalog => catalog
        .AddFromAssemblyOf<OrderPlacedSchema>()             // the order events, the actions and their descriptors
        .AddContextProvider<OrderProvider>("order")        // the subject of order events, loaded when a scheduled action is due
        .AddContextProvider<CustomerProvider>("customer")
        .AddContextProvider<OrderHistoryProvider>("orderHistory"))
    .Configure(engine =>
    {
        // A reminder that runs more than 10 seconds late (the scheduler was down, or the order came in after the reminder's
        // time) is skipped rather than sent late.
        engine.SkipOverdueActions = true;
        engine.OverdueGracePeriod = TimeSpan.FromSeconds(10);
    }));

// The shop, and where it hands events to RuleLoom.
builder.Services
    .AddSingleton(TimeProvider.System)
    .AddSingleton<OrderStore>()
    .AddSingleton<CustomerDirectory>()
    .AddScoped<RuleStore>()
    .AddScoped<RuleRunner>();

// Scheduled actions run on Quartz.
builder.Services
    .AddSingleton<ActionScheduler>()
    .AddQuartz()
    .AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

// What the demo page shows.
builder.Services
    .AddSingleton<ActivityFeed>()
    .AddSingleton<Outbox>();

// Traces, metrics and logs over OTLP, when an endpoint is set: the "dashboard" launch profile sends them to the Aspire
// Dashboard (see the README). RuleLoom's spans nest under each request's; a scheduled run is a trace of its own.
if (builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] is not null)
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("demo-shop"))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddSource(RuleLoomDiagnostics.Name))
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddMeter(RuleLoomDiagnostics.Name))
        .WithLogging()
        .UseOtlpExporter();
}

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();     // the demo page, wwwroot/index.html

app.MapOrders();          // the shop's API: place, pay and ship orders
app.MapDemo();            // what the demo page reads

// What a rule-authoring UI uses: GET /rules/catalog and POST /rules/validate. A real app secures them: .RequireAuthorization().
app.MapRuleLoom("/rules");

app.Run();
