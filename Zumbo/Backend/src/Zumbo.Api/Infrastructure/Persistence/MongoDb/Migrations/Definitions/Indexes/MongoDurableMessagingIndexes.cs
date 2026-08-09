using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoDurableMessagingIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "WorkItems",
            "outbox_messages",
            "ux_outbox_owner_event_deduplication",
            new BsonDocument { ["OwnerModule"] = 1, ["EventType"] = 1, ["DeduplicationKey"] = 1 },
            Unique: true,
            PartialFilter: new BsonDocument("DeduplicationKey", new BsonDocument("$type", "string"))),
        new(
            "WorkItems",
            "outbox_messages",
            "ix_outbox_pending_claim",
            new BsonDocument { ["Status"] = 1, ["AvailableAtUtc"] = 1, ["OccurredAtUtc"] = 1, ["_id"] = 1 }),
        new(
            "WorkItems",
            "outbox_messages",
            "ix_outbox_expired_lease",
            new BsonDocument { ["Status"] = 1, ["LeaseUntilUtc"] = 1, ["OccurredAtUtc"] = 1, ["_id"] = 1 }),
        new(
            "WorkItems",
            "outbox_messages",
            "ix_outbox_dead_letter",
            new BsonDocument { ["Status"] = 1, ["DeadLetteredAtUtc"] = -1, ["_id"] = 1 }),
        new(
            "WorkItems",
            "inbox_messages",
            "ix_inbox_consumer_processed",
            new BsonDocument { ["ConsumerName"] = 1, ["ProcessedAtUtc"] = -1, ["_id"] = 1 }),
        new(
            "Audit",
            "auditlogs",
            "ux_auditlogs_deduplication_key",
            new BsonDocument("DeduplicationKey", 1),
            Unique: true,
            PartialFilter: new BsonDocument("DeduplicationKey", new BsonDocument("$type", "string")))
    ];
}
