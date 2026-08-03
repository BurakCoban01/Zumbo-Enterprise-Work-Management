using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoCapacityPlanIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "WorkItems",
            "capacity_plans",
            "ix_capacity_plans_tenant_owner_state",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["OwnerUserId"] = 1,
                ["Archived"] = 1,
                ["UpdatedAt"] = -1,
                ["_id"] = 1
            }),
        new(
            "WorkItems",
            "capacity_plans",
            "ix_capacity_plans_tenant_viewers",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["ViewerUserIds"] = 1,
                ["Archived"] = 1,
                ["_id"] = 1
            }),
        new(
            "WorkItems",
            "capacity_plans",
            "ix_capacity_plans_tenant_projects",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["ProjectIds"] = 1,
                ["Archived"] = 1,
                ["_id"] = 1
            })
    ];
}
