using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoPrivacyWorkflowIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "Identity",
            "privacyworkflows",
            "ix_privacy_workflows_owner_state",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["RequestedByUserId"] = 1,
                ["State"] = 1,
                ["_id"] = 1
            }),
        new(
            "Identity",
            "privacyworkflows",
            "ix_privacy_workflows_retention",
            new BsonDocument { ["ExpiresAt"] = 1, ["_id"] = 1 })
    ];
}
