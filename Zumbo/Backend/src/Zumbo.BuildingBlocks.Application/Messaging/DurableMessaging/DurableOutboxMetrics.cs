namespace Zumbo.BuildingBlocks.Application.Messaging;

public sealed record DurableOutboxMetrics(
    long Pending,
    long Processing,
    long DeadLetter,
    long Completed,
    long Retried,
    DateTimeOffset? OldestPendingAtUtc,
    DateTimeOffset CapturedAtUtc);
