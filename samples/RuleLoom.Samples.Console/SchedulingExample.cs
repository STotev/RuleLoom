using RuleLoom.Abstractions.Action;
using RuleLoom.Abstractions.Condition;
using RuleLoom.Abstractions.Rule;
using RuleLoom.Abstractions.Scheduling;
using RuleLoom.Actions;
using RuleLoom.Engine;

namespace RuleLoom.Samples.Console;

/// <summary>
/// Scheduled actions: RuleLoom computes when they should run (ScheduledFor) and returns them instead of executing
/// them; dispatching them later is up to your scheduler.
/// </summary>
internal static class SchedulingExample
{
    public static async Task RunAsync()
    {
        var rule = new RuleDefinition
        {
            Id = "invoice-issued",
            Name = "Invoice issued",
            EventType = "InvoiceIssued",
            Condition = new OperatorCondition
            {
                Operator = "eq",
                Operands = [new PathOperand { Path = "Status" }, new ValueOperand { Value = "Open" }]
            },
            Actions =
            [
                new ActionDefinition { Action = "console-log", Parameters = { ["message"] = "Invoice sent." } },
                new ActionDefinition
                {
                    // 2 days before the due date.
                    Action = "console-log",
                    Parameters = { ["message"] = "Reminder: payment due soon." },
                    Schedule = new ScheduleMetadata { Mode = ScheduleMode.Advance, Amount = 2, Unit = ScheduleUnit.Days, ReferencePath = "DueAt" }
                },
                new ActionDefinition
                {
                    // 1 day after the event.
                    Action = "console-log",
                    Parameters = { ["message"] = "Confirm the invoice was received." },
                    Schedule = new ScheduleMetadata { Mode = ScheduleMode.Delay, Amount = 1, Unit = ScheduleUnit.Days }
                }
            ]
        };

        var catalog = new CatalogBuilder().AddAction<ConsoleLogAction>().Build();
        var engine = new RuleEngine(catalog);

        var invoice = new Invoice
        {
            Number = "INV-2031",
            Amount = 1_250m,
            Status = InvoiceStatus.Open,
            DueAt = new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero)
        };

        var result = await engine.ExecuteAsync("InvoiceIssued", invoice, [rule]);

        ResultPrinter.Print("Scheduling: Delay and Advance", result);

        foreach (var scheduled in result.ScheduledActions)
        {
            System.Console.WriteLine(
                $"Dispatch '{scheduled.Action.Action}' (\"{scheduled.Action.GetParameter<string>("message")}\") at {scheduled.Result.ScheduledFor:u}");
        }

        System.Console.WriteLine();
    }
}
