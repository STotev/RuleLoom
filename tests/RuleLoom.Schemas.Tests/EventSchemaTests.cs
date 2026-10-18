using System.Text.Json;
using System.Text.Json.Serialization;

namespace RuleLoom.Schemas.Tests;

public class EventSchemaTests
{
    private static readonly EventSchemaDefinition Order = new OrderPlacedSchema().Build();

    [Fact]
    public void Build_DescribesTheEvent()
    {
        Assert.Equal("OrderPlaced", Order.EventType);
        Assert.Equal("Order placed", Order.Label);
        Assert.Equal("A customer placed an order.", Order.Description);
    }

    [Fact]
    public void Build_EventWithoutLabel_GetsOneFromItsType()
    {
        var schema = new LineAddedSchema().Build();

        Assert.Equal("Line added", schema.Label);
        Assert.Null(schema.Description);
    }

    [Theory]
    [InlineData("total", FieldType.Number, false)]
    [InlineData("quantity", FieldType.Integer, false)]
    [InlineData("paid", FieldType.Boolean, false)]
    [InlineData("number", FieldType.String, false)]
    [InlineData("note", FieldType.String, true)]
    [InlineData("status", FieldType.Integer, false)]
    [InlineData("placedAt", FieldType.DateTime, false)]
    [InlineData("dueOn", FieldType.Date, true)]
    [InlineData("id", FieldType.String, false)]
    [InlineData("customer", FieldType.Object, false)]
    [InlineData("lines", FieldType.Array, false)]
    [InlineData("attributes", FieldType.Object, false)]
    public void Build_GeneratesFieldsFromTheSerializedForm(string name, FieldType type, bool nullable)
    {
        var field = Field(Order.Fields, name);

        Assert.Equal((type, nullable), (field.Type, field.IsNullable));
    }

    [Fact]
    public void Build_NestedObjectsAndArrays_AreDescribed()
    {
        Assert.Equal(["tier", "vatID", "internalId", "orders"], Field(Order.Fields, "customer").Fields.Select(f => f.Name));

        var lines = Field(Order.Fields, "lines");
        Assert.Equal(FieldType.Object, lines.Items!.Type);
        Assert.Equal("sku", Assert.Single(lines.Items.Fields).Name);

        var tags = Field(Order.Fields, "tags");
        Assert.Equal((FieldType.String, false), (tags.Items!.Type, tags.Items.IsNullable));

        Assert.Empty(Field(Order.Fields, "attributes").Fields);
    }

    [Fact]
    public void Build_RepeatedType_StopsAsObjectWithoutFields()
    {
        var orders = Field(Field(Order.Fields, "customer").Fields, "orders");

        Assert.Equal(FieldType.Object, orders.Items!.Type);
        Assert.Empty(orders.Items.Fields);
    }

    [Fact]
    public void Build_Labels_AreConfiguredOrComeFromNames()
    {
        var customer = Field(Order.Fields, "customer");

        Assert.Equal("Order total", Field(Order.Fields, "total").Label);
        Assert.Equal("Placed at", Field(Order.Fields, "placedAt").Label);
        Assert.Equal("Vat ID", Field(customer.Fields, "vatID").Label);
        Assert.Equal("Loyalty tier at the time of the order.", Field(customer.Fields, "tier").Description);
    }

    [Fact]
    public void Build_IgnoredAndUnserializedFields_AreLeftOut()
    {
        Assert.DoesNotContain(Order.Fields, f => f.Name is "internalNotes" or "secret");
    }

    [Fact]
    public void Build_NumbersAreNotTypedAsStrings()
    {
        Assert.Contains("\"type\": \"number\"", Order.ToJson());
        Assert.DoesNotContain("pattern", Order.ToJson());
    }

    [Fact]
    public void Build_StringEnumConverter_DescribesEnumNames()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

        var status = Field(new OrderPlacedSchema().Build(options).Fields, "status");

        Assert.Equal(FieldType.Enum, status.Type);
        Assert.Equal(["Placed", "Paid"], status.EnumValues);
    }

    [Fact]
    public void Build_CustomNamingPolicy_IsFollowed()
    {
        var schema = new OrderPlacedSchema().Build(new JsonSerializerOptions());

        Assert.Contains(schema.Fields, f => f.Name == "Total" && f.Label == "Order total");
    }

    [Fact]
    public void Build_Contexts_AreDescribed()
    {
        var customer = Assert.Single(Order.Contexts, c => c.Name == "customer");
        var watchers = Assert.Single(Order.Contexts, c => c.Name == "watchers");

        Assert.Equal(("Customer", "The customer who placed the order.", false), (customer.Label, customer.Description, customer.IsMany));
        Assert.Equal("Loyalty tier", Field(customer.Fields, "tier").Label);
        Assert.DoesNotContain(customer.Fields, f => f.Name == "internalId");

        Assert.Equal(("Watchers", true), (watchers.Label, watchers.IsMany));
        Assert.Equal(["email", "manager"], watchers.Fields.Select(f => f.Name));
    }

    [Fact]
    public void Build_WithoutEvent_Throws()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new UnnamedSchema().Build());

        Assert.Contains("Event(", error.Message);
    }

    [Fact]
    public void Event_NamedTwice_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new TwiceNamedSchema());
    }

    [Fact]
    public void Field_NotAPropertyChain_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ComputedFieldSchema());
    }

    [Fact]
    public void Context_DeclaredTwice_Throws()
    {
        Assert.Throws<ArgumentException>(() => new DuplicateContextSchema());
    }

    [Fact]
    public void Build_ConfiguredFieldThatIsNotSerialized_Throws()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new JsonIgnoredFieldSchema().Build());

        Assert.Contains("'secret' isn't serialized", error.Message);
    }

    [Fact]
    public void Build_ConfiguredFieldBelowARepeatedType_Throws()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new BelowCycleSchema().Build());

        Assert.Contains("below a repeated type", error.Message);
    }

    [Fact]
    public void Field_ConfiguredTwice_KeepsBothSettings()
    {
        var total = Field(new TwiceConfiguredSchema().Build().Fields, "total");

        Assert.Equal(("Total amount", "Including tax."), (total.Label, total.Description));
    }

    private static FieldDefinition Field(IEnumerable<FieldDefinition> fields, string name) => Assert.Single(fields, f => f.Name == name);

    private sealed class UnnamedSchema : EventSchema<LineDto>;

    private sealed class TwiceNamedSchema : EventSchema<LineDto>
    {
        public TwiceNamedSchema()
        {
            Event("A");
            Event("B");
        }
    }

    private sealed class ComputedFieldSchema : EventSchema<OrderDto>
    {
        public ComputedFieldSchema()
        {
            Event("Computed");
            Field(o => o.Total + 1);
        }
    }

    private sealed class DuplicateContextSchema : EventSchema<OrderDto>
    {
        public DuplicateContextSchema()
        {
            Event("Duplicate");
            Context<CustomerDto>("customer");
            Context<CustomerDto>("Customer");
        }
    }

    private sealed class JsonIgnoredFieldSchema : EventSchema<OrderDto>
    {
        public JsonIgnoredFieldSchema()
        {
            Event("Ignored");
            Field(o => o.Secret).Label("Secret");
        }
    }

    private sealed class BelowCycleSchema : EventSchema<UserDto>
    {
        public BelowCycleSchema()
        {
            Event("UserChanged");
            Field(u => u.Manager!.Email).Label("Manager's email");
            Field(u => u.Manager!.Manager!.Email).Label("Their manager's email");
        }
    }

    private sealed class TwiceConfiguredSchema : EventSchema<OrderDto>
    {
        public TwiceConfiguredSchema()
        {
            Event("Twice");
            Field(o => o.Total).Label("Total amount");
            Field(o => o.Total).Description("Including tax.");
        }
    }
}
