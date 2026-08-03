using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoNotificationDeliveryIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new("Notifications", "notifications", "ux_notifications_deduplication_key",
            new BsonDocument { ["OrganizationId"] = 1, ["DeduplicationKey"] = 1 },
            Unique: true,
            PartialFilter: new BsonDocument("DeduplicationKey", new BsonDocument("$type", "string"))),
        new("Notifications", "notifications", "ix_notifications_email_status_next_attempt",
            new BsonDocument
            {
                ["EmailStatus"] = 1,
                ["EmailNextAttemptAt"] = 1,
                ["EmailLeaseUntil"] = 1,
                ["OrganizationId"] = 1,
                ["_id"] = 1
            })
    ];
}
