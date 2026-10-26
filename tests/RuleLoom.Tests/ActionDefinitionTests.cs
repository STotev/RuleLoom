using RuleLoom.Abstractions;
using RuleLoom.Abstractions.Action;

namespace RuleLoom.Tests;

public class ActionDefinitionTests
{
    private static readonly ActionDefinition Action = new()
    {
        Action = "send-email",
        Parameters =
        {
            ["to"] = "ada@example.com",
            ["amount"] = 100,
            ["rate"] = 0.5,
            ["sendAt"] = "2026-05-01T14:00:00+02:00",
            ["urgent"] = true,
            ["status"] = "Active",
            ["recipients"] = new List<object?> { "a@example.com", "b@example.com" },
            ["note"] = null
        }
    };

    [Fact]
    public void GetParameter_SameType_ReturnsValue()
    {
        Assert.Equal("ada@example.com", Action.GetParameter<string>("to"));
        Assert.True(Action.GetParameter<bool>("urgent"));
    }

    [Fact]
    public void GetParameter_ConvertsWithSharedRules()
    {
        Assert.Equal(100m, Action.GetParameter<decimal>("amount"));
        Assert.Equal(100L, Action.GetParameter<long>("amount"));
        Assert.Equal(0.5m, Action.GetParameter<decimal>("rate"));
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero), Action.GetParameter<DateTimeOffset>("sendAt"));
        Assert.Equal(Status.Active, Action.GetParameter<Status>("status"));
        Assert.Equal(100, Action.GetParameter<int?>("amount"));
    }

    [Fact]
    public void GetParameter_ListValue_IsReadableAsList()
    {
        Assert.Equal(["a@example.com", "b@example.com"], Action.GetParameter<IReadOnlyList<object?>>("recipients"));
    }

    [Fact]
    public void GetParameter_NullValue_ReturnsNullForReferenceAndNullableTypes()
    {
        Assert.Null(Action.GetParameter<string?>("note"));
        Assert.Null(Action.GetParameter<int?>("note"));
    }

    [Theory]
    [InlineData("TO")]
    [InlineData("To")]
    public void GetParameter_NameInOtherCase_FindsParameter(string name)
    {
        Assert.Equal("ada@example.com", Action.GetParameter<string>(name));
    }

    [Fact]
    public void GetParameter_NamesDifferingOnlyInCase_ExactWinsAndAmbiguousIsMissing()
    {
        var action = new ActionDefinition { Action = "t", Parameters = { ["To"] = "upper", ["to"] = "lower" } };

        Assert.Equal("upper", action.GetParameter<string>("To"));
        Assert.Equal("lower", action.GetParameter<string>("to"));
        Assert.Throws<KeyNotFoundException>(() => action.GetParameter<string>("TO"));
    }

    [Fact]
    public void GetParameter_Missing_ThrowsNamingActionAndParameter()
    {
        var ex = Assert.Throws<KeyNotFoundException>(() => Action.GetParameter<string>("cc"));

        Assert.Equal("Action 'send-email' has no parameter 'cc'.", ex.Message);
    }

    [Fact]
    public void GetParameter_Unconvertible_ThrowsDescriptiveError()
    {
        AssertUnconvertible(() => Action.GetParameter<int>("to"), "to", "String", "Int32");
        AssertUnconvertible(() => Action.GetParameter<string>("amount"), "amount", "Int32", "String");
        AssertUnconvertible(() => Action.GetParameter<int>("rate"), "rate", "Double", "Int32");
        AssertUnconvertible(() => Action.GetParameter<int>("note"), "note", "null", "Int32");
    }

    [Fact]
    public void TryGetParameter_ConvertibleValue_ReturnsTrue()
    {
        Assert.True(Action.TryGetParameter<decimal>("amount", out var amount));
        Assert.Equal(100m, amount);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("to")]
    public void TryGetParameter_MissingOrUnconvertible_ReturnsFalse(string name)
    {
        Assert.False(Action.TryGetParameter<int>(name, out _));
    }

    [Fact]
    public async Task GetParametersAsync_ReadsThemAsTheType_NamesIgnoringCase()
    {
        var action = new ActionDefinition
        {
            Action = "send-email",
            Parameters = { ["TO"] = "ada@example.com", ["amount"] = 100, ["status"] = "active", ["recipients"] = Action.Parameters["recipients"], ["extra"] = 1 }
        };

        var parameters = await action.GetParametersAsync<EmailParameters>(Context, TestContext.Current.CancellationToken);

        Assert.Equal(("ada@example.com", 100m, Status.Active, null), (parameters.To, parameters.Amount, parameters.Status, parameters.Note));
        Assert.Equal(["a@example.com", "b@example.com"], parameters.Recipients);
    }

    [Fact]
    public async Task GetParametersAsync_MissingRequired_ThrowsNamingIt()
    {
        var action = new ActionDefinition { Action = "send-email", Parameters = { ["amount"] = 1 } };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            action.GetParametersAsync<EmailParameters>(Context, TestContext.Current.CancellationToken).AsTask());

        Assert.StartsWith("The parameters of action 'send-email' can't be read as EmailParameters:", ex.Message);
        Assert.Contains("'to'", ex.Message);
    }

    [Theory]
    [InlineData("amount", "lots")]
    [InlineData("to", null)]
    [InlineData("status", "Archived")]
    public async Task GetParametersAsync_WrongValue_ThrowsNamingTheParameter(string name, object? value)
    {
        var action = new ActionDefinition { Action = "send-email", Parameters = { ["to"] = "ada@example.com", ["amount"] = 1, [name] = value } };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            action.GetParametersAsync<EmailParameters>(Context, TestContext.Current.CancellationToken).AsTask());

        Assert.StartsWith($"Parameter '{name}' of action 'send-email' can't be read as EmailParameters:", ex.Message);
    }

    [Fact]
    public async Task GetParametersAsync_Paths_ResolveOnTheStateAndLoadContexts()
    {
        var customer = new FakeContextProvider(new { Email = "ada@example.com" });
        var context = new TestCatalog().Provider("customer", customer).Event("OrderPlaced", "customer")
            .Context(new { Total = 640.5m }, new { Note = "repeat customer" }, "OrderPlaced");
        var action = new ActionDefinition
        {
            Action = "send-email",
            Parameters =
            {
                ["to"] = RulePath.Parse("@customer.Email"),
                ["amount"] = RulePath.Parse("Total"),
                ["note"] = RulePath.Parse("$previous.Note")
            }
        };

        var parameters = await action.GetParametersAsync<EmailParameters>(context, TestContext.Current.CancellationToken);

        Assert.Equal(("ada@example.com", 640.5m, "repeat customer"), (parameters.To, parameters.Amount, parameters.Note));
        Assert.Equal(1, customer.Calls);
    }

    [Fact]
    public async Task GetParametersAsync_PathToAContextTheEventDoesNotDeclare_ThrowsNamingTheParameter()
    {
        var action = new ActionDefinition { Action = "send-email", Parameters = { ["to"] = RulePath.Parse("@customer.Email"), ["amount"] = 1 } };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            action.GetParametersAsync<EmailParameters>(Context, TestContext.Current.CancellationToken).AsTask());

        Assert.StartsWith("Parameter 'to' of action 'send-email' can't be resolved ('@customer.Email'):", ex.Message);
    }

    [Fact]
    public async Task GetParameterAsync_ResolvesAPathOrReadsAValue()
    {
        var action = new ActionDefinition { Action = "send-email", Parameters = { ["amount"] = RulePath.Parse("Total"), ["to"] = "ada@example.com" } };
        var context = new EvaluationContext(new { Total = 640.5m });

        Assert.Equal(640.5m, await action.GetParameterAsync<decimal>("AMOUNT", context, TestContext.Current.CancellationToken));
        Assert.Equal("ada@example.com", await action.GetParameterAsync<string>("to", context, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void GetParameter_Path_ThrowsPointingAtTheAsyncRead()
    {
        var action = new ActionDefinition { Action = "send-email", Parameters = { ["to"] = RulePath.Parse("Customer.Email") } };

        var ex = Assert.Throws<InvalidOperationException>(() => action.GetParameter<string>("to"));

        Assert.Equal("Parameter 'to' of action 'send-email' is the path 'Customer.Email'; read it with GetParameterAsync.", ex.Message);
        Assert.False(action.TryGetParameter<string>("to", out _));
    }

    [Fact]
    public async Task TypedAction_RunAsAnAction_ReceivesItsParameters()
    {
        IAction action = new EmailAction();
        var definition = new ActionDefinition { Action = "send-email", Parameters = { ["to"] = "ada@example.com", ["amount"] = 5 } };

        var result = await action.ExecuteAsync(definition, Context, TestContext.Current.CancellationToken);

        Assert.Equal("ada@example.com: 5", result.Message);
    }

    private static readonly EvaluationContext Context = new(new { });

    public sealed record EmailParameters(string To, decimal Amount, Status Status = Status.Draft, string? Note = null, IReadOnlyList<string>? Recipients = null);

    private sealed class EmailAction : IAction<EmailParameters>
    {
        public Task<ActionResult> ExecuteAsync(EmailParameters parameters, ActionDefinition action, EvaluationContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ActionResult.Success($"{parameters.To}: {parameters.Amount}"));
    }

    private static void AssertUnconvertible(Func<object?> read, string name, string from, string to)
    {
        var ex = Assert.Throws<InvalidOperationException>(read);

        Assert.Equal($"Parameter '{name}' of action 'send-email' is {from}, which can't be converted to {to}.", ex.Message);
    }

    public enum Status
    {
        Draft,
        Active
    }
}
