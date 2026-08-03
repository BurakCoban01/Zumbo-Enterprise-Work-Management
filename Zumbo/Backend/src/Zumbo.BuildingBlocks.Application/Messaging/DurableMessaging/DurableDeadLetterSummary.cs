namespace Zumbo.BuildingBlocks.Application.Messaging;

public sealed record DurableDeadLetterSummary(
    string Id,
    string EventType,
    int Attempts,
    DateTimeOffset DeadLetteredAtUtc);
