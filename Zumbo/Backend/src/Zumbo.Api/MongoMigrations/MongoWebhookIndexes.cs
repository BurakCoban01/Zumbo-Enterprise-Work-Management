using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoWebhookIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "WorkItems",
            "webhooksubscriptions",
            "ix_webhook_subscriptions_tenant_active",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["IsActive"] = 1,
                ["_id"] = 1
            }),
        new(
            "WorkItems",
            "webhookdeliverys",
            "ix_webhook_deliveries_claim",
            new BsonDocument
            {
                ["Status"] = 1,
                ["NextAttemptAtUtc"] = 1,
                ["LeaseUntilUtc"] = 1,
                ["_id"] = 1
            }),
        new(
            "WorkItems",
            "webhookdeliverys",
            "ix_webhook_deliveries_tenant_subscription",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["SubscriptionId"] = 1,
                ["_id"] = 1
            }),
        new(
            "WorkItems",
            "webhookdeliverys",
            "ix_webhook_deliveries_tenant_status",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["Status"] = 1,
                ["_id"] = 1
            })
    ];
}
