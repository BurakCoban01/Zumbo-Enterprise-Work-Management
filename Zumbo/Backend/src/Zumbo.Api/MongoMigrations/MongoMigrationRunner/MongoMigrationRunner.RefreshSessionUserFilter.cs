using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static FilterDefinition<BsonDocument> RefreshSessionUserFilter(BsonValue checkpoint)
    {
        var filter = Builders<BsonDocument>.Filter.Exists("RefreshTokens.0", true);
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return filter;
    }
}
