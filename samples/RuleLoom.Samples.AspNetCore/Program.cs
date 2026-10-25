using Quartz;
using RuleLoom.Actions;
using RuleLoom.Engine;
using RuleLoom.Samples.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddRuleLoom(rules => rules.AddCatalog(catalog => catalog
        .AddFromAssemblyOf<OrderPlacedSchema>() // the event schemas and SendEmailAction
        .AddAction<ConsoleLogAction>()))        // built in, for development; not registered by default
    .AddScoped<TenantRuleStore>()
    .AddSingleton<ActionScheduler>()
    .AddQuartz()
    .AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

var app = builder.Build();

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
