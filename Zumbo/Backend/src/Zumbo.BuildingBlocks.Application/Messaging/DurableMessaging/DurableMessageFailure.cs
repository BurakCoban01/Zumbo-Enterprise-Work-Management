namespace Zumbo.BuildingBlocks.Application.Messaging;

public sealed record DurableMessageFailure(
    bool Updated,
    bool DeadLettered,
    int Attempt,
    DateTimeOffset? NextAttemptAtUtc);
