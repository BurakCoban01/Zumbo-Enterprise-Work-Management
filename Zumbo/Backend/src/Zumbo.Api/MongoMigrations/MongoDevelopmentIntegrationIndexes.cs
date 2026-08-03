using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoDevelopmentIntegrationIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "WorkItems",
            "developmentconnections",
            "ix_development_connections_tenant_updated",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["UpdatedAtUtc"] = -1,
                ["_id"] = 1
            }),
        new(
            "WorkItems",
            "developmentrepositorymappings",
            "ux_development_mappings_tenant_connection_repository",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["ConnectionId"] = 1,
                ["ExternalRepositoryId"] = 1
            },
            Unique: true),
        new(
            "WorkItems",
            "developmentrepositorymappings",
            "ix_development_mappings_tenant_project_active",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["ProjectId"] = 1,
                ["IsActive"] = 1,
                ["_id"] = 1
            }),
        new(
            "WorkItems",
            "workitemdevelopmentlinks",
            "ix_development_links_tenant_work_item_updated",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["ProjectId"] = 1,
                ["WorkItemId"] = 1,
                ["UpdatedAtUtc"] = -1,
                ["_id"] = 1
            }),
        new(
            "WorkItems",
            "workitemdevelopmentlinks",
            "ix_development_links_tenant_mapping_commit",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["MappingId"] = 1,
                ["CommitSha"] = 1,
                ["ExternalId"] = 1,
                ["_id"] = 1
            }),
        new(
            "WorkItems",
            "developmentwebhookreceipts",
            "ix_development_receipts_connection_expiry",
            new BsonDocument
            {
                ["ConnectionId"] = 1,
                ["ExpiresAtUtc"] = 1,
                ["_id"] = 1
            }),
        new(
            "WorkItems",
            "developmentwebhookreceipts",
            "ttl_development_receipts_expiry",
            new BsonDocument("ExpiresAtUtc", 1),
            ExpireAfter: TimeSpan.Zero)
    ];
}
