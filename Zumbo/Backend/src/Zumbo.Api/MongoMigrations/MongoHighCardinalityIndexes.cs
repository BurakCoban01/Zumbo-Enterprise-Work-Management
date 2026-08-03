using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoHighCardinalityIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "Identity",
            "refreshsessions",
            "ix_refreshsessions_owner_last_seen",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["UserId"] = 1,
                ["LastSeenAt"] = -1,
                ["_id"] = 1
            })
    ];
}
