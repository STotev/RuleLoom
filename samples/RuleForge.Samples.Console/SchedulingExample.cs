using RuleForge.Abstractions.Action;
using RuleForge.Abstractions.Condition;
using RuleForge.Abstractions.Rule;
using RuleForge.Abstractions.Scheduling;
using RuleForge.Actions;
using RuleForge.Engine;
using RuleForge.Evaluators;
using RuleForge.Operators;

namespace RuleForge.Samples.Console;

/// <summary>
/// Scheduled actions: RuleForge computes when they should run (ScheduledFor) and returns them instead of executing
/// them; dispatching them later is up to your scheduler.
/// </summary>
internal static class SchedulingExample
{
    public static async Task RunAsync()
    {
        var rule = new RuleDefinition
        {
            Name = "Viewing booked",
            EventType = "ViewingBooked",
            Condition = new OperatorCondition
            {
                Operator = "eq",
                Operands = [new PathOperand { Path = "Status" }, new ValueOperand { Value = "Active" }]
            },
            Actions =
            [
                new ActionDefinition { Action = "log", Parameters = { ["message"] = "Viewing confirmed." } },
                new ActionDefinition
                {
                    // 2 hours before the viewing.
                    Action = "log",
                    Parameters = { ["message"] = "Reminder: viewing soon." },
                    Schedule = new ScheduleMetadata { Mode = ScheduleMode.Advance, Amount = 2, Unit = ScheduleUnit.Hours, ReferencePath = "ViewingAt" }
                },
                new ActionDefinition
                {
                    // 1 day after the event.
                    Action = "log",
                    Parameters = { ["message"] = "Ask for feedback." },
                    Schedule = new ScheduleMetadata { Mode = ScheduleMode.Delay, Amount = 1, Unit = ScheduleUnit.Days }
                }
            ]
        };

        var engine = new RuleEngine(
            new ConditionEvaluator(BuiltInOperators.Create()),
            new DirectActionFactory([new LogAction()]));

        var property = new Property
        {
            Address = "1 Elm Street",
            Price = 430_000m,
            Status = ListingStatus.Active,
            ViewingAt = new DateTimeOffset(2026, 10, 10, 15, 0, 0, TimeSpan.Zero)
        };

        var result = await engine.ExecuteAsync("ViewingBooked", property, [rule]);

        ResultPrinter.Print("Scheduling: Delay and Advance", result);

        foreach (var scheduled in result.ScheduledActions)
        {
            System.Console.WriteLine(
                $"Dispatch '{scheduled.Action.Action}' (\"{scheduled.Action.GetParameter<string>("message")}\") at {scheduled.Result.ScheduledFor:u}");
        }

        System.Console.WriteLine();
    }
}
