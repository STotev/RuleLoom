using System.Text.Json.Nodes;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Context;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Engine;
using RuleLoom.Schemas;
using static RuleLoom.Tests.Conditions;

namespace RuleLoom.Tests;

public class ContextReferenceTests
{
    // The event's schema declares how it refers to each context (as written by EventSchema<T> classes). It comes from a
    // catalog store, so contexts without a registered provider fail only the rules that use them.
    private static readonly CatalogDocument Stored = new([EventSchemaDefinition.Parse("""
        { "x-ruleloom-event": "TransferMade", "type": "object",
          "properties": {
            "fromAccountId": { "type": "string" }, "toAccountId": { "type": "string" },
            "address": { "type": "object", "properties": { "country": { "type": "string" }, "postcode": { "type": "string" } } } },
          "x-ruleloom-contexts": {
            "fromAccount": { "x-ruleloom-referenced-by": "fromAccountId" },
            "toAccount": { "x-ruleloom-referenced-by": "toAccountId" },
            "zone": { "x-ruleloom-referenced-by": { "country": "address.country", "postcode": "address.postcode" } },
            "calendar": {},
            "payer": { "x-ruleloom-provider": "account", "x-ruleloom-referenced-by": "fromAccountId" },
            "payee": { "x-ruleloom-provider": "account", "x-ruleloom-referenced-by": "toAccountId" },
            "auditor": { "x-ruleloom-provider": "audit" } } }
        """)]);

    public static TheoryData<string> StateKinds() => ["object", "json"];

    [Theory]
    [MemberData(nameof(StateKinds))]
    public async Task ExecuteAsync_OneProviderUnderTwoNames_GetsEachContextsReference(string kind)
    {
        var accounts = new FakeContextProvider(request => new { Id = request.GetReference<string>(), Frozen = request.Name == "toAccount" });
        var engine = Engine(("fromAccount", accounts), ("toAccount", accounts));

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
        var engine = Engine(("zone", zones));

        var result = await engine.ExecuteAsync("TransferMade", Transfer(kind),
            [Rule("TransferMade", Op("eq", Path("@zone.Postcode"), Value("EH1 1AA")))], TestContext.Current.CancellationToken);

        Assert.Equal(RuleExecutionStatus.Matched, result.Rules[0].Status);
        var parts = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(zones.Requests.Single().Reference);
        Assert.Equal(("GB", "EH1 1AA"), ((string?)parts["COUNTRY"], (string?)parts["postcode"]));
    }

    [Fact]
    public async Task ExecuteAsync_ContextsLoadedByOneProvider_UseItWithEachReference()
    {
        var accounts = new FakeContextProvider(request => new { Id = request.GetReference<string>() });
        var engine = Engine(("account", accounts));

        var result = await engine.ExecuteAsync("TransferMade", Transfer("object"),
            [Rule("TransferMade", Op("and", Op("eq", Path("@payer.Id"), Value("A-1")), Op("eq", Path("@payee.Id"), Value("A-2"))))],
            TestContext.Current.CancellationToken);

        Assert.Equal(RuleExecutionStatus.Matched, result.Rules[0].Status);
        Assert.Equal(new Dictionary<string, object?> { ["payer"] = "A-1", ["payee"] = "A-2" },
            accounts.Requests.ToDictionary(r => r.Name, r => r.Reference));
    }

    [Fact]
    public async Task ExecuteAsync_ContextWhoseProviderIsNotRegistered_FailsOnlyItsRules()
    {
        var engine = Engine(("account", new FakeContextProvider(new { Id = "A-1" })));

        var result = await engine.ExecuteAsync("TransferMade", Transfer("object"),
            [Rule("TransferMade", Op("eq", Path("@auditor.Name"), Value("x")), "audited"), Rule("TransferMade", Op("eq", Path("@payer.Id"), Value("A-1")), "paid")],
            TestContext.Current.CancellationToken);

        var audited = result.Rules.Single(r => r.RuleId == "audited");
        Assert.Equal(RuleExecutionStatus.Failed, audited.Status);
        Assert.Equal("No context provider is registered under 'audit' (context '@auditor').", audited.Error!.Message);
        Assert.Equal(RuleExecutionStatus.Matched, result.Rules.Single(r => r.RuleId == "paid").Status);
    }

    [Fact]
    public async Task ExecuteAsync_ContextWithoutAReference_PassesNoReference()
    {
        var calendar = new FakeContextProvider(request => new { Id = request.Resolve("fromAccountId") });
        var engine = Engine(("calendar", calendar));

        var result = await engine.ExecuteAsync("TransferMade", Transfer("object"),
            [Rule("TransferMade", Op("eq", Path("@calendar.Id"), Value("A-1")))], TestContext.Current.CancellationToken);

        Assert.Equal(RuleExecutionStatus.Matched, result.Rules[0].Status);
        Assert.Null(calendar.Requests.Single().Reference);
    }

    [Fact]
    public async Task ExecuteAsync_ActionLoadingAContextOnDemand_GetsTheReference()
    {
        var accounts = new FakeContextProvider(request => request.Reference);
        object? loaded = null;
        var engine = new TestCatalog()
            .Action("load", new LoadingAction(async (context, ct) => loaded = await context.GetContextAsync("toAccount", ct)))
            .Provider("toAccount", accounts)
            .Stored(Stored)
            .Engine();

        await engine.ExecuteAsync("TransferMade", Transfer("object"),
            [new RuleDefinition { Id = "r", Name = "r", EventType = "TransferMade", Condition = Op("eq", Value(1), Value(1)), Actions = [new ActionDefinition { Action = "load" }] }],
            TestContext.Current.CancellationToken);

        Assert.Equal("A-2", loaded);
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

    private static RuleEngine Engine(params (string Name, IContextProvider Provider)[] providers)
    {
        var catalog = new TestCatalog().Action("record", new NoOpAction()).Stored(Stored);
        foreach (var (name, provider) in providers)
            catalog.Provider(name, provider);

        return catalog.Engine();
    }

    private static RuleDefinition Rule(string eventType, Abstractions.Condition.ConditionNode condition, string id = "r") =>
        new() { Id = id, Name = id, EventType = eventType, Condition = condition, Actions = [new ActionDefinition { Action = "record" }] };

    private sealed record ZoneKey(string Country, string Postcode);

    private sealed class LoadingAction(Func<EvaluationContext, CancellationToken, Task> run) : IAction
    {
        public async Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default)
        {
            await run(context, cancellationToken);
            return ActionResult.Success();
        }
    }

    private sealed class NoOpAction : IAction
    {
        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActionResult.Success());
    }
}
