using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static FilterDefinition<BsonDocument> ProjectVersionForId(BsonValue id, long version)
    {
        var versionFilter = version == 0
            ? Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Exists("Version", false),
                Builders<BsonDocument>.Filter.Eq("Version", 0),
                Builders<BsonDocument>.Filter.Type("Version", BsonType.Null))
            : Builders<BsonDocument>.Filter.Eq("Version", version);
        return Builders<BsonDocument>.Filter.Eq("_id", id) & versionFilter;
    }
}
