using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;

namespace RuleLoom.Samples.AspNetCore;

/// <summary>
/// Registered as "loyalty-check". Reads the customer's earlier orders (the <c>orderHistory</c> context) and logs how
/// many there are and what they add up to. No parameters.
/// </summary>
public sealed class LoyaltyCheckAction(ILogger<LoyaltyCheckAction> logger) : IAction
{
    public async Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default)
    {
        var orders = await context.GetContextAsync<IReadOnlyList<PastOrder>>("orderHistory", cancellationToken) ?? [];
        var message = $"{orders.Count} earlier orders worth {orders.Sum(o => o.Total):0.00}.";

        logger.LogInformation("Loyalty check: {Message}", message);

        return ActionResult.Success(message);
    }
}

public sealed class LoyaltyCheckDescriptor : ActionDescriptor<LoyaltyCheckAction>
{
    public LoyaltyCheckDescriptor()
    {
        Label("Loyalty check");
        Description("Reports the customer's earlier orders: how many and their total.");
    }
}
