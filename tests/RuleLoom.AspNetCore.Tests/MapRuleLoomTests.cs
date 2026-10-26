using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Schemas;

namespace RuleLoom.AspNetCore.Tests;

public class MapRuleLoomTests
{
    private const string Valid = """
        { "id": "big-order", "name": "Big order", "eventType": "OrderPlaced",
          "condition": { "operator": "gte", "operands": [{ "path": "Total" }, { "value": 500 }] },
          "actions": [{ "action": "notify" }] }
        """;

    private const string UnknownField = """
        { "id": "typo", "name": "Typo", "eventType": "OrderPlaced",
          "condition": { "operator": "gte", "operands": [{ "path": "Totl" }, { "value": 500 }] } }
        """;

    private const string NoCondition = """{ "id": "broken", "name": "Broken", "eventType": "OrderPlaced" }""";

    [Fact]
    public async Task Catalog_ReturnsTheCatalogDocument()
    {
        await using var app = await StartAsync();

        var response = await app.GetTestClient().GetAsync("/rules/catalog", TestContext.Current.CancellationToken);
        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("OrderPlaced", (string?)document["events"]![0]!["x-ruleloom-event"]);
        Assert.Contains(document["actions"]!.AsArray(), action => (string?)action!["name"] == "notify");
    }

    [Fact]
    public async Task Validate_ValidRule_ReturnsItsResult()
    {
        var result = await ValidateAsync(Valid);

        AssertJson("""{"id":"big-order","parsed":true,"isValid":true,"errors":[],"warnings":[]}""", result);
    }

    [Fact]
    public async Task Validate_InvalidRule_ReturnsItsErrorsWithPaths()
    {
        var result = await ValidateAsync(UnknownField);

        Assert.False((bool)result["isValid"]!);
        Assert.Equal("$.condition.operands[0].path", (string?)result["errors"]![0]!["path"]);
        Assert.Equal("'Totl' is not a field of OrderPlaced.", (string?)result["errors"]![0]!["message"]);
    }

    [Fact]
    public async Task Validate_MalformedRule_ReturnsTheParseError()
    {
        var result = await ValidateAsync(NoCondition);

        AssertJson("""{"parsed":false,"isValid":false,"errors":[{"path":"$.condition","message":"'condition' is required."}],"warnings":[]}""",
            result);
    }

    [Fact]
    public async Task Validate_Array_ReturnsEveryRulesResultInOrder_WithPathsRelativeToTheRule()
    {
        var results = (await ValidateAsync($"[ {Valid}, {NoCondition}, {UnknownField} ]")).AsArray();

        Assert.Equal([0, 1, 2], results.Select(r => (int)r!["index"]!));
        Assert.Equal(["big-order", null, "typo"], results.Select(r => (string?)r!["id"]));
        Assert.Equal([true, false, true], results.Select(r => (bool)r!["parsed"]!));
        Assert.Equal("$.condition", (string?)results[1]!["errors"]![0]!["path"]);
        Assert.Equal("$.condition.operands[0].path", (string?)results[2]!["errors"]![0]!["path"]);
    }

    [Theory]
    [InlineData("{ not json", "The body isn't valid JSON.")]
    [InlineData("42", "Expected a rule (a JSON object) or an array of rules.")]
    public async Task Validate_NotARuleOrArray_IsABadRequest(string body, string title)
    {
        await using var app = await StartAsync();

        var response = await PostAsync(app, body);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(title, (string?)problem["title"]);
    }

    [Fact]
    public async Task MapRuleLoom_ReturnsTheGroup_ForTheAppsConventions()
    {
        await using var app = await StartAsync(group => group.AddEndpointFilter((_, _) => ValueTask.FromResult<object?>(Results.Unauthorized())));

        var catalog = await app.GetTestClient().GetAsync("/rules/catalog", TestContext.Current.CancellationToken);
        var validate = await PostAsync(app, Valid);

        Assert.Equal(HttpStatusCode.Unauthorized, catalog.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, validate.StatusCode);
    }

    private static void AssertJson(string expected, JsonNode actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), actual), $"Expected {expected}, got {actual.ToJsonString()}");

    private static async Task<JsonNode> ValidateAsync(string body)
    {
        await using var app = await StartAsync();

        var response = await PostAsync(app, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
    }

    private static Task<HttpResponseMessage> PostAsync(WebApplication app, string body) =>
        app.GetTestClient().PostAsync("/rules/validate", new StringContent(body, Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

    private static async Task<WebApplication> StartAsync(Action<RouteGroupBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRuleLoom(rules => rules.AddCatalog(catalog => catalog
            .AddEvent<OrderPlacedSchema>()
            .AddAction<NotifyAction>()));

        var app = builder.Build();
        var group = app.MapRuleLoom("/rules");
        configure?.Invoke(group);

        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    public sealed class Order
    {
        public decimal Total { get; set; }
    }

    public sealed class OrderPlacedSchema : EventSchema<Order>
    {
        public OrderPlacedSchema() => Event("OrderPlaced");
    }

    public sealed class NotifyAction : IAction
    {
        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActionResult.Success());
    }
}
