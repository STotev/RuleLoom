namespace RuleLoom.Samples.AspNetCore.Demo;

public sealed record Email(DateTimeOffset At, string To, string Subject);

/// <summary>The emails <c>send-email</c> "sent", newest first, for the demo page.</summary>
public sealed class Outbox(TimeProvider time)
{
    private readonly LinkedList<Email> _emails = [];

    public void Add(string to, string subject)
    {
        lock (_emails)
            _emails.AddFirst(new Email(time.GetUtcNow(), to, subject));
    }

    public IReadOnlyList<Email> All()
    {
        lock (_emails)
            return _emails.ToList();
    }
}
