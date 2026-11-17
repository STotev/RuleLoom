using OpenTelemetry.Metrics;
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

// Logs through Serilog; the levels are in appsettings.json.
builder.Services.AddSerilog((services, logging) => logging
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services));

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

// Uncomment for RuleLoom's spans, nested under each request's, and its metrics on the console (left out because they
// bury the logs); a real app uses an exporter such as OTLP.
// builder.Services.AddOpenTelemetry()
//     .WithTracing(tracing => tracing
//         .AddAspNetCoreInstrumentation()
//         .AddSource(RuleLoomDiagnostics.Name)
//         .AddConsoleExporter())
//     .WithMetrics(metrics => metrics
//         .AddMeter(RuleLoomDiagnostics.Name)
//         .AddConsoleExporter());

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();     // the demo page, wwwroot/index.html

app.MapOrders();          // the shop's API: place, pay and ship orders
app.MapDemo();            // what the demo page reads

// What a rule-authoring UI uses: GET /rules/catalog and POST /rules/validate. A real app secures them: .RequireAuthorization().
app.MapRuleLoom("/rules");

app.Run();
