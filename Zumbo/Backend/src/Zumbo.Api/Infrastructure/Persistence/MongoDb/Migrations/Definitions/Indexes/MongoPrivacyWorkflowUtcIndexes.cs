using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoPrivacyWorkflowUtcIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "Identity",
            "privacyworkflows",
            "ix_privacy_workflows_retention_utc",
            new BsonDocument { ["ExpiresAtUtc"] = 1, ["_id"] = 1 })
    ];
}
