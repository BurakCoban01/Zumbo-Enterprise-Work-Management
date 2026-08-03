using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static async Task EnsureRefreshSessionMatchesAsync(
        IMongoCollection<BsonDocument> sessions,
        BsonDocument expected,
        CancellationToken cancellationToken)
    {
        var filter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Eq("_id", expected["_id"]),
            Builders<BsonDocument>.Filter.Eq("TokenHash", expected["TokenHash"]));
        var actual = await sessions.Find(filter).FirstOrDefaultAsync(cancellationToken);
        if (actual is null
            || actual.GetValue("_id", BsonNull.Value) != expected["_id"]
            || actual.GetValue("UserId", BsonNull.Value) != expected["UserId"]
            || actual.GetValue("OrganizationId", BsonNull.Value) != expected["OrganizationId"]
            || actual.GetValue("TokenHash", BsonNull.Value) != expected["TokenHash"])
        {
            throw new InvalidOperationException(
                $"Refresh session '{expected["_id"]}' conflicts with incompatible stored ownership or token data.");
        }
    }
}
