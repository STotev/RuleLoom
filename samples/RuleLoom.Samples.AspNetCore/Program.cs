using Quartz;
using RuleLoom.Abstractions;
using RuleLoom.Actions;
using RuleLoom.Engine;
using RuleLoom.Samples.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddRuleLoom(rules => rules.AddCatalog(catalog => catalog
        .AddFromAssemblyOf<OrderPlacedSchema>() // the event schemas, SendEmailAction and LoyaltyCheckAction
        .AddContextProvider<CustomerProvider>("customer")
        .AddContextProvider<OrderHistoryProvider>("orderHistory")
        .AddAction<ConsoleLogAction>()))        // built in, for development; not registered by default
    .AddSingleton<CustomerDirectory>()
    .AddScoped<TenantRuleStore>()
    .AddSingleton<ActionScheduler>()
    .AddQuartz()
    .AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

var app = builder.Build();

// What a rule-authoring UI reads: the events and their contexts, their fields, the operators and the actions.
app.MapGet("/catalog", (Catalog catalog) => Results.Text(catalog.ToJson(), "application/json"));

app.MapPost("/events", async (
    EventRequest request,
    TenantRuleStore ruleStore,
    RuleEngine engine,
    ActionScheduler scheduler,
    CancellationToken cancellationToken) =>
{
    var rules = await ruleStore.LoadAsync(request.TenantId, cancellationToken);
    if (rules is null)
        return Results.NotFound(new { error = $"Unknown tenant '{request.TenantId}'." });

    var result = await engine.ExecuteAsync(request.EventType, request.Current, request.Previous, rules.Valid, cancellationToken);
    var jobs = await scheduler.ScheduleAsync(request, result.ScheduledActions, cancellationToken);

    return Results.Ok(EventResponse.From(result, rules.Invalid, jobs));
});

app.Run();
