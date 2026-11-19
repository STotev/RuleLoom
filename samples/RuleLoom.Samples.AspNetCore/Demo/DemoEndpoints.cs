using RuleLoom.Samples.AspNetCore.Customers;
using RuleLoom.Samples.AspNetCore.Rules;

namespace RuleLoom.Samples.AspNetCore.Demo;

/// <summary>What the demo page reads: the customers, the activity, the emails sent and the rules as loaded.</summary>
public static class DemoEndpoints
{
    public static void MapDemo(this IEndpointRouteBuilder app)
    {
        var demo = app.MapGroup("/demo");

        demo.MapGet("/customers", (CustomerDirectory customers) => customers.All);
        demo.MapGet("/activity", (ActivityFeed activity) => activity.Latest());
        demo.MapGet("/emails", (Outbox outbox) => outbox.All());
        demo.MapGet("/rules", async (RuleStore rules, CancellationToken cancellationToken) =>
        {
            var loaded = await rules.LoadAsync(cancellationToken);
            return new
            {
                valid = loaded.Valid.Select(r => new { r.Id, r.Name, r.EventType }),
                invalid = loaded.Invalid
            };
        });
    }
}
