using MongoDB.Driver;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

namespace Zumbo.BuildingBlocks.Infrastructure.Messaging;

public sealed class MongoOutboxDocument
{
    public string Id { get; set; } = string.Empty;
    public string OwnerModule { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? DeduplicationKey { get; set; }
    public string Payload { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string Status { get; set; } = DurableMessageStates.Pending;
    public int AttemptCount { get; set; }
    public DateTime AvailableAtUtc { get; set; }
    public string? LeaseOwner { get; set; }
    public string? LeaseToken { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public string? LastError { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? DeadLetteredAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public static MongoOutboxDocument From(DurableEventEnvelope message) => new()
    {
        Id = message.Id,
        OwnerModule = message.OwnerModule,
        EventType = message.EventType,
        SchemaVersion = message.SchemaVersion,
        TenantId = message.TenantId,
        CorrelationId = message.CorrelationId,
        DeduplicationKey = message.DeduplicationKey,
        Payload = message.Payload,
        OccurredAtUtc = message.OccurredAtUtc.UtcDateTime,
        AvailableAtUtc = message.OccurredAtUtc.UtcDateTime,
        UpdatedAtUtc = message.OccurredAtUtc.UtcDateTime
    };

    public DurableEventLease ToLease() => new(
        new DurableEventEnvelope(
            Id,
            OwnerModule,
            EventType,
            SchemaVersion,
            TenantId,
            CorrelationId,
            DeduplicationKey,
            Payload,
            new DateTimeOffset(DateTime.SpecifyKind(OccurredAtUtc, DateTimeKind.Utc))),
        AttemptCount,
        LeaseOwner!,
        LeaseToken!,
        new DateTimeOffset(DateTime.SpecifyKind(LeaseUntilUtc!.Value, DateTimeKind.Utc)));
}
