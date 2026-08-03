namespace Zumbo.BuildingBlocks.Application.Messaging;

public sealed record DurableEventLease(
    DurableEventEnvelope Event,
    int Attempt,
    string WorkerId,
    string LeaseToken,
    DateTimeOffset LeaseUntilUtc);
