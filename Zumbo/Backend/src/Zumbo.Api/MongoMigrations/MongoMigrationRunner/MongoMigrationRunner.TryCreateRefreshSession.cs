using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static bool TryCreateRefreshSession(
        BsonValue value,
        string userId,
        string organizationId,
        out BsonDocument session)
    {
        session = null!;
        if (!value.IsBsonDocument
            || string.IsNullOrWhiteSpace(userId)
            || string.IsNullOrWhiteSpace(organizationId))
        {
            return false;
        }

        var token = value.AsBsonDocument;
        var sessionId = StringValue(token, "SessionId");
        var tokenHash = StringValue(token, "TokenHash");
        if (string.IsNullOrWhiteSpace(sessionId)
            || string.IsNullOrWhiteSpace(tokenHash)
            || !TryResolveUtc(token.GetValue("ExpiresAt", BsonNull.Value), out var expiresAt))
        {
            return false;
        }

        var createdAt = token.GetValue("CreatedAt", token["ExpiresAt"]);
        var revokedValue = token.GetValue("RevokedAt", BsonNull.Value);
        BsonValue revokedAtUtc = BsonNull.Value;
        var retainBase = expiresAt;
        if (TryResolveUtc(revokedValue, out var revokedAt))
        {
            revokedAtUtc = revokedAt;
            if (revokedAt > retainBase)
            {
                retainBase = revokedAt;
            }
        }

        session = new BsonDocument
        {
            ["_id"] = sessionId,
            ["UserId"] = userId,
            ["OrganizationId"] = organizationId,
            ["TokenHash"] = tokenHash,
            ["CreatedAt"] = createdAt,
            ["ExpiresAt"] = token["ExpiresAt"],
            ["ExpiresAtUtc"] = expiresAt,
            ["RevokedAt"] = revokedValue,
            ["RevokedAtUtc"] = revokedAtUtc,
            ["ReplacedBySessionId"] = BsonNull.Value,
            ["RetainUntilUtc"] = retainBase.AddDays(30),
            ["Version"] = 1L
        };
        return true;
    }
}
