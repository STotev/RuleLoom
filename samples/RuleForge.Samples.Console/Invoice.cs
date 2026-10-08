namespace RuleForge.Samples.Console;

/// <summary>
/// An invoice awaiting payment; the scheduling example runs against it.
/// </summary>
public sealed record Invoice
{
    public required string Number { get; init; }
    public decimal Amount { get; init; }
    public InvoiceStatus Status { get; init; }
    public DateTimeOffset? DueAt { get; init; }
}

public enum InvoiceStatus
{
    Draft,
    Open,
    Paid
}
