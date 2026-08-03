using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoGoalIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "Projects",
            "goals",
            "ix_goals_tenant_owner_state",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["OwnerUserId"] = 1,
                ["Archived"] = 1,
                ["UpdatedAt"] = -1,
                ["_id"] = 1
            }),
        new(
            "Projects",
            "goals",
            "ix_goals_tenant_viewers",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["ViewerUserIds"] = 1,
                ["Archived"] = 1,
                ["_id"] = 1
            }),
        new(
            "Projects",
            "goals",
            "ix_goals_tenant_key_result_owners",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["KeyResults.OwnerUserId"] = 1,
                ["Archived"] = 1,
                ["_id"] = 1
            })
    ];
}
