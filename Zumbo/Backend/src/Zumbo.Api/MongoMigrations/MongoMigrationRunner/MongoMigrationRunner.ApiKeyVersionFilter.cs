using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static FilterDefinition<BsonDocument> ApiKeyVersionFilter(BsonValue checkpoint)
    {
        var filter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("Version", false),
            Builders<BsonDocument>.Filter.Lte("Version", 0),
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Exists("ExpiresAt", true),
                Builders<BsonDocument>.Filter.Exists("ExpiresAtUtc", false)),
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Ne("RevokedAt", BsonNull.Value),
                Builders<BsonDocument>.Filter.Exists("RevokedAtUtc", false)));
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return filter;
    }
}
