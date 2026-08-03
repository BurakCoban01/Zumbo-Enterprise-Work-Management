using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static FilterDefinition<BsonDocument> ProjectLifecycleFilter(BsonValue checkpoint)
    {
        var filter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("Version", false),
            Builders<BsonDocument>.Filter.Lte("Version", 0),
            Builders<BsonDocument>.Filter.Type("Version", BsonType.Null),
            Builders<BsonDocument>.Filter.Exists("Visibility", false),
            Builders<BsonDocument>.Filter.Exists("Templates", false),
            Builders<BsonDocument>.Filter.Exists("Components", false),
            Builders<BsonDocument>.Filter.Exists("Versions", false),
            Builders<BsonDocument>.Filter.Exists("Releases", false),
            Builders<BsonDocument>.Filter.Exists("Milestones", false),
            Builders<BsonDocument>.Filter.Exists("ArchivedAt", false),
            Builders<BsonDocument>.Filter.Exists("RetainUntil", false));
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return filter;
    }
}
