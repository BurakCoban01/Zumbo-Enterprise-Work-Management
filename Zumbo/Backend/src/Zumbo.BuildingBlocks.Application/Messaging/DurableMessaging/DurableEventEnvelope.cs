namespace Zumbo.BuildingBlocks.Application.Messaging;

public sealed record DurableEventEnvelope(
    string Id,
    string OwnerModule,
    string EventType,
    int SchemaVersion,
    string TenantId,
    string CorrelationId,
    string? DeduplicationKey,
    string Payload,
    DateTimeOffset OccurredAtUtc)
{
    public static DurableEventEnvelope Create(
        string ownerModule,
        string eventType,
        int schemaVersion,
        string tenantId,
        string correlationId,
        string payload,
        DateTimeOffset occurredAtUtc,
        string? deduplicationKey = null) =>
        new(
            Guid.NewGuid().ToString("N"),
            Required(ownerModule, nameof(ownerModule)),
            Required(eventType, nameof(eventType)),
            schemaVersion > 0 ? schemaVersion : throw new ArgumentOutOfRangeException(nameof(schemaVersion)),
            Required(tenantId, nameof(tenantId)),
            Required(correlationId, nameof(correlationId)),
            string.IsNullOrWhiteSpace(deduplicationKey) ? null : deduplicationKey.Trim(),
            Required(payload, nameof(payload)),
            occurredAtUtc);

    private static string Required(string value, string parameterName) =>
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ArgumentException("A durable event value cannot be empty.", parameterName);
}
