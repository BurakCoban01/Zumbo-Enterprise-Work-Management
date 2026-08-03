using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoPortfolioIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "Projects",
            "portfolios",
            "ix_portfolios_tenant_owner_state",
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
            "portfolios",
            "ix_portfolios_tenant_viewers",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["ViewerUserIds"] = 1,
                ["Archived"] = 1,
                ["_id"] = 1
            }),
        new(
            "Projects",
            "portfolios",
            "ix_portfolios_tenant_projects",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["Initiatives.ProjectIds"] = 1,
                ["Archived"] = 1,
                ["_id"] = 1
            })
    ];
}
