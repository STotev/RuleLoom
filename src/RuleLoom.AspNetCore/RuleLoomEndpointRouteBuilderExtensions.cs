using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RuleLoom.Abstractions;
using RuleLoom.AspNetCore;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Maps RuleLoom's authoring endpoints.
/// </summary>
public static class RuleLoomEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the endpoints a rule-authoring UI uses, under <paramref name="prefix"/>:
    /// <list type="bullet">
    /// <item><c>GET {prefix}/catalog</c>: the catalog document.</item>
    /// <item><c>POST {prefix}/validate</c>: validates one rule (a JSON object) or an array of rules, rule by rule, and
    /// returns a <see cref="RuleValidationResponse"/> per rule (one, or an array in order).</item>
    /// </list>
    /// Rules aren't stored. Requires <c>AddRuleLoom</c>.
    /// </summary>
    /// <returns>The route group, to add authorization or other conventions to (<c>RequireAuthorization()</c>).</returns>
    public static RouteGroupBuilder MapRuleLoom(this IEndpointRouteBuilder endpoints, string prefix)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(prefix);

        var group = endpoints.MapGroup(prefix);

        group.MapGet("/catalog", (Catalog catalog) => Results.Text(catalog.ToJson(), "application/json"));
        group.MapPost("/validate", ValidateEndpoint.HandleAsync);

        return group;
    }
}
