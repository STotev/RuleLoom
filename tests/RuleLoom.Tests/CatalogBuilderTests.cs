using System.Text.Json;
using System.Text.Json.Nodes;
using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Context;
using RuleLoom.Actions;
using RuleLoom.Operators;
using RuleLoom.Schemas;

namespace RuleLoom.Tests;

public class CatalogBuilderTests
{
    [Fact]
    public void Build_Empty_HasTheBuiltInOperators()
    {
        var catalog = new CatalogBuilder().Build();

        Assert.Equal(["eq", "changed", "and", "or", "not", "gt", "gte", "lt", "lte"], catalog.Operators.Select(o => o.Name));
        Assert.Empty(catalog.Events);
        Assert.Empty(catalog.Actions);
    }

    [Fact]
    public void Operators_ApplyInCallOrder()
    {
        var catalog = new CatalogBuilder()
            .RemoveOperator("EQ")
            .AddOperator<CustomEquals>()
            .AddOperator<CustomEquals>()
            .RemoveBuiltInOperators()
            .Build();

        Assert.IsType<CustomEquals>(Assert.Single(catalog.Operators));
    }

    [Fact]
    public void Build_ConflictingOperator_Throws()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new CatalogBuilder().AddOperator(new CustomEquals()).Build());

        Assert.Contains("Operator 'eq' is registered more than once", error.Message);
    }

    [Fact]
    public void AddAction_NamesByRegistration_AndFindsTheDescriptorNextToTheAction()
    {
        var catalog = new CatalogBuilder()
            .AddAction<SendEmailAction>()
            .AddAction<SendEmailAction>()
            .AddAction<SendEmailAction>("notify")
            .AddAction<RenamedAction>()
            .Build();

        Assert.Equal(
        [
            new ActionRegistration("send-email", typeof(SendEmailAction), "Send email", "Sends a templated email."),
            new ActionRegistration("notify", typeof(SendEmailAction), "Send email", "Sends a templated email."),
            new ActionRegistration("sms", typeof(RenamedAction))
        ], catalog.Actions);
    }

    [Fact]
    public void AddAction_WithADescriptor_UsesIt()
    {
        var catalog = new CatalogBuilder().AddAction<SendEmailAction, ShortEmailDescriptor>().Build();

        Assert.Equal("Email", Assert.Single(catalog.Actions).Label);
    }

    [Fact]
    public void AddAction_Untyped_HasNoParameters()
    {
        Assert.Null(Assert.Single(new CatalogBuilder().AddAction<SendEmailAction>().Build().Actions).Parameters);
    }

    [Fact]
    public void AddAction_TypedWithoutADescriptor_IsDescribedByItsParametersType()
    {
        var action = Assert.Single(new CatalogBuilder().AddAction<NotifyAction>().Build().Actions);

        Assert.Null(action.Label);
        Assert.Equal(["channel", "message"], action.Parameters!.Fields.Select(f => f.Name));
    }

    [Fact]
    public void AddAction_TypedWithAParametersDescriptor_AddsItsLabels()
    {
        var action = Assert.Single(new CatalogBuilder().AddAction<RemindAction>().Build().Actions);
        var parameters = JsonNode.Parse(action.Parameters!.ToJson())!;

        Assert.Equal("Remind", action.Label);
        Assert.Equal(["message"], action.Parameters!.Fields.Select(f => f.Name));
        Assert.Equal("Reminder text", action.Parameters.Fields[0].Label);
        Assert.Equal(["message"], parameters["required"]!.AsArray().Select(n => (string?)n));
    }

    [Fact]
    public void AddAction_TypedWithAnUntypedDescriptor_KeepsItsLabelAndInfersParameters()
    {
        var action = Assert.Single(new CatalogBuilder().AddAction<NotifyAction, NotifyLabelDescriptor>().Build().Actions);

        Assert.Equal("Notify", action.Label);
        Assert.Equal(["channel", "message"], action.Parameters!.Fields.Select(f => f.Name));
    }

    [Fact]
    public void Build_ActionWithTwoParametersTypes_Throws()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new CatalogBuilder().AddAction<TwoParametersAction>().Build());

        Assert.Contains("TwoParametersAction takes several parameters types", error.Message);
    }

    [Fact]
    public void ToDocument_WritesActionParameters()
    {
        var json = JsonNode.Parse(new CatalogBuilder().AddAction<RemindAction>().Build().ToJson())!;

        Assert.Equal("Reminder text", (string?)json["actions"]![0]!["parameters"]!["properties"]!["message"]!["title"]);
    }

    [Fact]
    public void Build_ActionDescribedTwice_Throws()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new CatalogBuilder().AddAction<TwiceDescribedAction>().Build());

        Assert.Contains("TwiceDescribedAction is described more than once", error.Message);
    }

    [Fact]
    public void Build_SameNameForTwoActions_Throws()
    {
        var builder = new CatalogBuilder().AddAction<SendEmailAction>().AddAction<RenamedAction>("SEND-EMAIL");

        Assert.Contains("Action 'SEND-EMAIL' is registered more than once", Assert.Throws<InvalidOperationException>(builder.Build).Message);
    }

    [Fact]
    public void AddContextProvider_FindsTheSchemaOfWhatItLoads()
    {
        var catalog = new CatalogBuilder()
            .AddContextProvider<CustomerProvider>("customer")
            .AddContextProvider<CustomerListProvider>("customers")
            .Build();

        var customer = catalog.ContextProviders[0].Schema!;
        Assert.Equal(("customer", "Customer", false), (customer.Name, customer.Label, customer.IsMany));
        Assert.Equal(["id", "tier"], customer.Fields.Select(f => f.Name));
        Assert.Equal("Loyalty tier", customer.Fields[1].Label);
        Assert.True(catalog.ContextProviders[1].Schema!.IsMany);
    }

    [Fact]
    public void AddContextProvider_WithoutASchemaClass_DescribesTheTypeAlone()
    {
        var schema = new CatalogBuilder().AddContextProvider<AddressProvider>("address").Build().ContextProviders[0].Schema!;

        Assert.Null(schema.Label);
        Assert.Equal(["city"], schema.Fields.Select(f => f.Name));
    }

    [Fact]
    public void AddContextProvider_UntypedProviders_HaveNoSchema()
    {
        var catalog = new CatalogBuilder()
            .AddContextProvider<FakeContextProvider>("legacy")
            .AddContextProvider<JsonProvider>("json")
            .Build();

        Assert.All(catalog.ContextProviders, provider => Assert.Null(provider.Schema));
    }

    [Fact]
    public void AddContextProvider_WithASchemaOfAnotherType_Throws()
    {
        var builder = new CatalogBuilder().AddContextProvider<AddressProvider, CustomerSchema>("address");

        Assert.Equal("CustomerSchema describes Customer, but context provider 'address' loads Address.",
            Assert.Throws<InvalidOperationException>(builder.Build).Message);
    }

    [Fact]
    public void AddContextProvider_OfACollectionType_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new CatalogBuilder().AddContextProvider<ListProvider>("list").Build());
    }

    [Fact]
    public void UseJsonOptions_NamesEveryFieldWhenCalledLast()
    {
        var catalog = new CatalogBuilder()
            .AddEvent<OrderPlacedSchema>()
            .AddContextProvider<CustomerProvider>("customer")
            .UseJsonOptions(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper })
            .Build();

        Assert.Equal(["CUSTOMER_ID"], catalog.Events[0].Fields.Select(f => f.Name));
        Assert.Equal(["ID", "TIER"], catalog.ContextProviders[0].Schema!.Fields.Select(f => f.Name));
    }

    [Fact]
    public void AddFromAssemblyOf_AddsItsActionsUnderTheirDefaultNames_Once()
    {
        var catalog = new CatalogBuilder().AddFromAssemblyOf<ConsoleLogAction>().AddFromAssemblyOf<ConsoleLogAction>().Build();

        Assert.Equal([new ActionRegistration("console-log", typeof(ConsoleLogAction))], catalog.Actions);
    }

    [Fact]
    public async Task BuildAsync_MergesTheStoreAndSavesTheResult()
    {
        var store = new InMemoryCatalogStore();
        var ct = TestContext.Current.CancellationToken;
        await store.SaveAsync(new CatalogDocument([TestCatalog.Schema("Shipped")]), ct);

        var catalog = await new CatalogBuilder().AddEvent<OrderPlacedSchema>().AddContextProvider<CustomerProvider>("customer")
            .AddAction<SendEmailAction>().BuildAsync(store, ct);

        Assert.Equal(["OrderPlaced", "Shipped"], catalog.Events.Select(e => e.EventType));
        var saved = await store.LoadAsync(ct);
        Assert.Equal(["Shipped", "OrderPlaced"], saved.Events.Select(e => e.EventType));
        Assert.Equal(["customer"], saved.Contexts.Select(c => c.Name));
        Assert.Equal(9, saved.Operators.Count);
        Assert.Equal(["send-email"], saved.Actions.Select(a => a.Name));
    }

    public sealed record Customer(string Id, string Tier);

    public sealed record Address(string City);

    public sealed record OrderPlaced(string CustomerId);

    public sealed class OrderPlacedSchema : EventSchema<OrderPlaced>
    {
        public OrderPlacedSchema()
        {
            Event("OrderPlaced");
            Context("customer").ReferencedBy(o => o.CustomerId);
        }
    }

    public sealed class CustomerSchema : ContextSchema<Customer>
    {
        public CustomerSchema()
        {
            Label("Customer");
            Field(c => c.Tier).Label("Loyalty tier");
        }
    }

    public sealed class CustomerProvider : ContextProvider<Customer>
    {
        protected override ValueTask<Customer?> LoadAsync(ContextRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<Customer?>(null);
    }

    public sealed class CustomerListProvider : CollectionContextProvider<Customer>
    {
        protected override ValueTask<IReadOnlyList<Customer>> LoadAsync(ContextRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<Customer>>([]);
    }

    public sealed class AddressProvider : ContextProvider<Address>
    {
        protected override ValueTask<Address?> LoadAsync(ContextRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<Address?>(null);
    }

    public sealed class JsonProvider : ContextProvider<JsonNode>
    {
        protected override ValueTask<JsonNode?> LoadAsync(ContextRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<JsonNode?>(null);
    }

    public sealed class ListProvider : ContextProvider<List<Customer>>
    {
        protected override ValueTask<List<Customer>?> LoadAsync(ContextRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<List<Customer>?>(null);
    }

    public sealed class SendEmailAction : IAction
    {
        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(ActionResult.Success());
    }

    public sealed class SendEmailDescriptor : ActionDescriptor<SendEmailAction>
    {
        public SendEmailDescriptor()
        {
            Label("Send email");
            Description("Sends a templated email.");
        }
    }

    // Private, so discovery (which sees public types) doesn't find it next to SendEmailDescriptor.
    private sealed class ShortEmailDescriptor : ActionDescriptor<SendEmailAction>
    {
        public ShortEmailDescriptor() => Label("Email");
    }

    [ActionName("sms")]
    public sealed class RenamedAction : IAction
    {
        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(ActionResult.Success());
    }

    public sealed class TwiceDescribedAction : IAction
    {
        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(ActionResult.Success());
    }

    public sealed record NotifyParameters(string Channel, string Message);

    public sealed class NotifyAction : IAction<NotifyParameters>
    {
        public Task<ActionResult> ExecuteAsync(NotifyParameters parameters, ActionDefinition action, EvaluationContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(ActionResult.Success());
    }

    // Private, so discovery doesn't find it next to NotifyAction.
    private sealed class NotifyLabelDescriptor : ActionDescriptor<NotifyAction>
    {
        public NotifyLabelDescriptor() => Label("Notify");
    }

    public sealed class RemindParameters
    {
        public required string Message { get; init; }
        public string? TrackingId { get; init; }
    }

    public sealed class RemindAction : IAction<RemindParameters>
    {
        public Task<ActionResult> ExecuteAsync(RemindParameters parameters, ActionDefinition action, EvaluationContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(ActionResult.Success());
    }

    public sealed class RemindDescriptor : ActionDescriptor<RemindAction, RemindParameters>
    {
        public RemindDescriptor()
        {
            Label("Remind");
            Parameter(p => p.Message).Label("Reminder text");
            Parameter(p => p.TrackingId).Ignore();
        }
    }

    public sealed class TwoParametersAction : IAction<NotifyParameters>, IAction<RemindParameters>
    {
        public Task<ActionResult> ExecuteAsync(ActionDefinition action, EvaluationContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(ActionResult.Success());

        public Task<ActionResult> ExecuteAsync(NotifyParameters parameters, ActionDefinition action, EvaluationContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(ActionResult.Success());

        public Task<ActionResult> ExecuteAsync(RemindParameters parameters, ActionDefinition action, EvaluationContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(ActionResult.Success());
    }

    public sealed class FirstDescriptor : ActionDescriptor<TwiceDescribedAction>;

    public sealed class SecondDescriptor : ActionDescriptor<TwiceDescribedAction>;

    public sealed class CustomEquals : IConditionOperator
    {
        public string Name => "eq";
        public int MinOperands => 2;
        public int MaxOperands => 2;
        public bool Evaluate(OperatorContext context) => true;
    }
}
