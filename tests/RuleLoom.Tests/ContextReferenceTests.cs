using System.Text.Json.Nodes;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Context;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Actions;
using RuleLoom.Contexts;
using RuleLoom.Engine;
using static RuleLoom.Tests.Conditions;

namespace RuleLoom.Tests;

public class ContextReferenceTests
{
    private static readonly ContextReference FromAccount = new("fromAccountId", new Dictionary<string, string>());
    private static readonly ContextReference ToAccount = new("toAccountId", new Dictionary<string, string>());
    private static readonly ContextReference Zone = new(null, new Dictionary<string, string> { ["country"] = "address.country", ["postcode"] = "address.postcode" });

    public static TheoryData<string> StateKinds() => ["object", "json"];

    [Theory]
    [MemberData(nameof(StateKinds))]
    public async Task ExecuteAsync_OneProviderUnderTwoNames_GetsEachContextsReference(string kind)
    {
        var accounts = new FakeContextProvider(request => new { Id = request.GetReference<string>(), Frozen = request.Name == "toAccount" });
        var engine = Engine(new FakeReferences(("TransferMade", "fromAccount", FromAccount), ("TransferMade", "toAccount", ToAccount)),
            ("fromAccount", accounts), ("toAccount", accounts));

        var result = await engine.ExecuteAsync("TransferMade", Transfer(kind),
            [Rule("TransferMade", Op("and", Op("eq", Path("@fromAccount.Id"), Value("A-1")), Op("eq", Path("@toAccount.Frozen"), Value(true))))],
            TestContext.Current.CancellationToken);

        Assert.Equal(RuleExecutionStatus.Matched, result.Rules[0].Status);
        Assert.Equal(new Dictionary<string, object?> { ["fromAccount"] = "A-1", ["toAccount"] = "A-2" },
            accounts.Requests.ToDictionary(r => r.Name, r => r.Reference));
    }

    [Theory]
    [MemberData(nameof(StateKinds))]
    public async Task ExecuteAsync_CompositeReference_PassesThePartsByName(string kind)
    {
        var zones = new FakeContextProvider(request => request.GetReference<ZoneKey>());
        var engine = Engine(new FakeReferences(("TransferMade", "zone", Zone)), ("zone", zones));

        var result = await engine.ExecuteAsync("TransferMade", Transfer(kind),
            [Rule("TransferMade", Op("eq", Path("@zone.Postcode"), Value("EH1 1AA")))], TestContext.Current.CancellationToken);

        Assert.Equal(RuleExecutionStatus.Matched, result.Rules[0].Status);
        var parts = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(zones.Requests.Single().Reference);
        Assert.Equal(("GB", "EH1 1AA"), ((string?)parts["COUNTRY"], (string?)parts["postcode"]));
    }

    [Fact]
    public async Task ExecuteAsync_WithoutReferences_PassesNoReference()
    {
        var accounts = new FakeContextProvider(request => new { Id = request.Resolve("fromAccountId") });
        var withoutSource = Engine(null, ("fromAccount", accounts));
        var withoutDeclaration = Engine(new FakeReferences(), ("fromAccount", accounts));

        foreach (var engine in new[] { withoutSource, withoutDeclaration })
        {
            var result = await engine.ExecuteAsync("TransferMade", Transfer("object"),
                [Rule("TransferMade", Op("eq", Path("@fromAccount.Id"), Value("A-1")))], TestContext.Current.CancellationToken);

            Assert.Equal(RuleExecutionStatus.Matched, result.Rules[0].Status);
        }

        Assert.All(accounts.Requests, request => Assert.Null(request.Reference));
    }

    [Fact]
    public async Task GetContextAsync_OnDemand_GetsTheReference()
    {
        var accounts = new FakeContextProvider(request => request.Reference);
        var context = new EvaluationContext(Transfer("object"), null, "TransferMade",
            new DirectContextProviderFactory().Add(accounts, "toAccount"), new FakeReferences(("TransferMade", "toAccount", ToAccount)));

        Assert.Equal("A-2", await context.GetContextAsync("toAccount", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void GetReference_ConvertsSingleValues()
    {
        var guid = Guid.NewGuid();

        Assert.Equal("42", Request(42L).GetReference<string>());
        Assert.Equal(guid, Request(guid.ToString()).GetReference<Guid>());
        Assert.Equal(42, Request(42L).GetReference<int>());
        Assert.Null(Request(null).GetReference<string>());
        Assert.Throws<InvalidCastException>(() => Request("not a number").GetReference<int>());
    }

    [Fact]
    public void GetReference_MapsCompositePartsIgnoringCase()
    {
        var request = Request(new Dictionary<string, object?> { ["country"] = "GB", ["POSTCODE"] = "EH1 1AA" });

        Assert.Equal(new ZoneKey("GB", "EH1 1AA"), request.GetReference<ZoneKey>());
    }

    private static ContextRequest Request(object? reference) => new("zone", "TransferMade", null, null) { Reference = reference };

    private static object Transfer(string kind) => kind == "json"
        ? JsonNode.Parse("""{ "fromAccountId": "A-1", "toAccountId": "A-2", "address": { "country": "GB", "postcode": "EH1 1AA" } }""")!
        : new { FromAccountId = "A-1", ToAccountId = "A-2", Address = new { Country = "GB", Postcode = "EH1 1AA" } };

    private static RuleEngine Engine(IContextReferenceSource? references, params (string Name, IContextProvider Provider)[] providers)
    {
        var factory = new DirectContextProviderFactory();
        foreach (var (name, provider) in providers)
            factory.Add(provider, name);

        return new RuleEngine(Evaluator(), new DirectActionFactory().Add(new NoOpAction(), "record"), null, factory, references);
    }

    private static RuleDefinition Rule(string eventType, Abstractions.Condition.ConditionNode condition) =>
        new() { Id = "r", Name = "r", EventType = eventType, Condition = condition, Actions = [new ActionDefinition { Action = "record" }] };

    private sealed record ZoneKey(string Country, string Postcode);

    private sealed class FakeReferences(params (string EventType, string Context, ContextReference Reference)[] references) : IContextReferenceSource
    {
        public ValueTask<ContextReference?> GetReferenceAsync(string eventType, string contextName, CancellationToken cancellationToken) =>
            ValueTask.FromResult(references
                .Where(r => string.Equals(r.EventType, eventType, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(r.Context, contextName, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Reference)
                .FirstOrDefault());
    }

    private sealed class NoOpAction : IAction
    {
        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActionResult.Success());
    }
}
