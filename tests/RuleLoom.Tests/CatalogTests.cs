using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Context;
using RuleLoom.Operators;
using RuleLoom.Schemas;

namespace RuleLoom.Tests;

public class CatalogTests
{
    // Declares a context by the provider's name, one loaded by another provider, and one referenced by a field.
    private static readonly EventSchemaDefinition Transfer = EventSchemaDefinition.Parse("""
        { "x-ruleloom-event": "TransferMade", "title": "Transfer made", "type": "object",
          "properties": { "toAccountId": { "type": "string" } },
          "x-ruleloom-contexts": {
            "calendar": {},
            "toAccount": { "x-ruleloom-provider": "account", "x-ruleloom-referenced-by": "toAccountId" } } }
        """);

    private static readonly ContextProviderRegistration Account = new("account", typeof(FakeContextProvider));
    private static readonly ContextProviderRegistration Calendar = new("calendar", typeof(FakeContextProvider));

    [Fact]
    public void Constructor_BrokenCodeEntries_ThrowListingEveryProblem()
    {
        var broken = EventSchemaDefinition.Parse("""
            { "x-ruleloom-event": "Shipped", "type": "object",
              "x-ruleloom-contexts": { "carrier": {}, "parcel": { "x-ruleloom-provider": "calendar", "x-ruleloom-referenced-by": "parcelId" } } }
            """);

        var error = Assert.Throws<InvalidOperationException>(() => new Catalog(
            [Transfer, broken, TestCatalog.Schema("TRANSFERMADE")],
            [Account, Calendar],
            [new EqualsOperator(), new CountingOperator("EQ", true), new CountingOperator("@at", true)],
            [new ActionRegistration("log", typeof(object)), new ActionRegistration("", typeof(object))]));

        Assert.Equal(string.Join(Environment.NewLine,
            "The catalog is invalid:",
            "- Operator 'EQ' is registered more than once (names ignore case).",
            "- Operator name '@at' can't be used in rules: names starting with '$', '@' or '#' are reserved for paths.",
            "- Action names can't be empty.",
            "- Event 'TRANSFERMADE' is registered more than once (names ignore case).",
            "- Event 'Shipped': No context provider is registered under 'carrier' (context '@carrier').",
            "- Event 'Shipped': Context '@parcel' is referenced by 'parcelId', which isn't a field of Shipped."), error.Message);
    }

    [Fact]
    public void Constructor_BrokenStoredEntries_BecomeProblems()
    {
        var stored = new CatalogDocument(
            [EventSchemaDefinition.Parse("""{ "x-ruleloom-event": "Shipped", "type": "object", "x-ruleloom-contexts": { "carrier": {} } }""")],
            [ContextSchemaDefinition.Parse("""{ "x-ruleloom-context": "crm", "type": "object" }""")]);

        var catalog = new Catalog([Transfer], [Account, Calendar], [], [], stored);

        Assert.Equal(
        [
            new CatalogProblem(CatalogEntryKind.Context, "crm", "No context provider is registered under 'crm'."),
            new CatalogProblem(CatalogEntryKind.Event, "Shipped", "No context provider is registered under 'carrier' (context '@carrier').")
        ], catalog.Problems);
        Assert.False(catalog.TryGetContext("Shipped", "carrier", out _, out var error));
        Assert.Equal("No context provider is registered under 'carrier' (context '@carrier').", error);
    }

    [Fact]
    public void Constructor_CodeWinsOverStoredEntries()
    {
        var storedTransfer = EventSchemaDefinition.Parse("""{ "x-ruleloom-event": "transfermade", "title": "Old", "type": "object" }""");
        var storedShipped = TestCatalog.Schema("Shipped");
        var accountSchema = ContextSchemaDefinition.Parse("""{ "x-ruleloom-context": "account", "title": "Stored account", "type": "object" }""");
        var calendarSchema = ContextSchemaDefinition.Parse("""{ "x-ruleloom-context": "calendar", "title": "Stored calendar", "type": "object" }""");
        var codeAccount = Account with { Schema = ContextSchemaDefinition.Parse("""{ "x-ruleloom-context": "account", "title": "Account", "type": "object" }""") };

        var catalog = new Catalog([Transfer], [codeAccount, Calendar], [], [], new CatalogDocument(
            [storedTransfer, storedShipped], [accountSchema, calendarSchema],
            [new CatalogOperator("between", null, null, 3, 3)], [new CatalogAction("send-sms", null, null)]));

        Assert.Equal([Transfer, storedShipped], catalog.Events);
        Assert.Equal(["Account", "Stored calendar"], catalog.ContextProviders.Select(p => p.Schema!.Label));
        Assert.Empty(catalog.Operators);
        Assert.Empty(catalog.Actions);
    }

    [Fact]
    public void TryGetContext_FindsTheDeclarationAndItsProvider()
    {
        var catalog = new Catalog([Transfer], [Account, Calendar], [], []);

        Assert.True(catalog.TryGetContext("transfermade", "TOACCOUNT", out var toAccount, out _));
        Assert.Equal(("toAccount", "account"), (toAccount.Declaration.Name, toAccount.Provider.Name));
        Assert.True(catalog.TryGetContext("TransferMade", "calendar", out var calendar, out _));
        Assert.Same(Calendar, calendar.Provider);
    }

    [Theory]
    [InlineData("TransferMade", "account", "'@account' is not a context of TransferMade.")]
    [InlineData("Unknown", "calendar", "'@calendar' can't be loaded: event 'Unknown' isn't in the catalog, so it declares no contexts.")]
    public void TryGetContext_UndeclaredOrUnknownEvent_GivesTheReason(string eventType, string name, string message)
    {
        var catalog = new Catalog([Transfer], [Account, Calendar], [], []);

        Assert.False(catalog.TryGetContext(eventType, name, out var context, out var error));
        Assert.Null(context);
        Assert.Equal(message, error);
    }

    [Fact]
    public void Lookups_IgnoreCase()
    {
        var catalog = new Catalog([Transfer], [Account, Calendar], [new EqualsOperator()], [new ActionRegistration("send-email", typeof(object))]);

        Assert.True(catalog.TryGetEvent("TRANSFERMADE", out _));
        Assert.True(catalog.TryGetContextProvider("Account", out _));
        Assert.True(catalog.TryGetOperator("EQ", out _));
        Assert.True(catalog.TryGetAction("Send-Email", out _));
        Assert.False(catalog.TryGetAction("unknown", out _));
    }

    [Fact]
    public void ToDocument_DescribesEveryEntry()
    {
        var schema = ContextSchemaDefinition.Parse("""{ "x-ruleloom-context": "account", "type": "object" }""");
        var catalog = new Catalog([Transfer], [Account with { Schema = schema }, Calendar], [new EqualsOperator(), new AndOperator()],
            [new ActionRegistration("send-email", typeof(object), "Send email", "Sends a templated email.")]);

        var document = CatalogDocument.Parse(catalog.ToJson());

        Assert.Equal(["TransferMade"], document.Events.Select(e => e.EventType));
        Assert.Equal(["account"], document.Contexts.Select(c => c.Name));
        Assert.Equal(
        [
            new CatalogOperator("eq", "equals", new EqualsOperator().Description, 2, 2),
            new CatalogOperator("and", "all of", "True when every operand is true.", 1, null)
        ], document.Operators);
        Assert.Equal([new CatalogAction("send-email", "Send email", "Sends a templated email.")], document.Actions);
        Assert.Empty(document.Problems);
    }

    [Fact]
    public void Operator_WithoutLabelOrDescription_DescribesNothing()
    {
        IConditionOperator counting = new CountingOperator("count", true);

        Assert.Null(counting.Label);
        Assert.Null(counting.Description);
    }
}
