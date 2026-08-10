using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

internal static class MongoLegacyIdentityCredentialIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
        MongoIdentityCredentialIndexes.All.Select(specification => specification.Name switch
        {
            "ix_refreshsessions_owner_active" => specification with
            {
                Keys = new BsonDocument
                {
                    ["OrganizationId"] = 1,
                    ["UserId"] = 1,
                    ["RevokedAt"] = 1,
                    ["ExpiresAt"] = 1,
                    ["_id"] = 1
                }
            },
            "ix_apikeys_owner_revoked_expires" => specification with
            {
                Keys = new BsonDocument
                {
                    ["OrganizationId"] = 1,
                    ["UserId"] = 1,
                    ["RevokedAt"] = 1,
                    ["ExpiresAt"] = 1,
                    ["_id"] = 1
                }
            },
            _ => specification
        }).ToList();
}
