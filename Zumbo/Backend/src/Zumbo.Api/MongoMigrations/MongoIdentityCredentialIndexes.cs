using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoIdentityCredentialIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "Identity",
            "refreshsessions",
            "ux_refreshsessions_token_hash",
            new BsonDocument("TokenHash", 1),
            Unique: true),
        new(
            "Identity",
            "refreshsessions",
            "ix_refreshsessions_owner_active",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["UserId"] = 1,
                ["RevokedAtUtc"] = 1,
                ["ExpiresAtUtc"] = 1,
                ["_id"] = 1
            }),
        new(
            "Identity",
            "refreshsessions",
            "ttl_refreshsessions_retain_until_utc",
            new BsonDocument("RetainUntilUtc", 1),
            ExpireAfter: TimeSpan.Zero),
        new(
            "Identity",
            "apikeys",
            "ix_apikeys_owner_created",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["UserId"] = 1,
                ["CreatedAt"] = -1,
                ["_id"] = 1
            }),
        new(
            "Identity",
            "apikeys",
            "ix_apikeys_owner_revoked_expires",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["UserId"] = 1,
                ["RevokedAtUtc"] = 1,
                ["ExpiresAtUtc"] = 1,
                ["_id"] = 1
            })
    ];
}
