using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static FilterDefinition<BsonDocument> WorkItemActivityVersionFilter() =>
        Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("ActivityStorageVersion", false),
            Builders<BsonDocument>.Filter.Lt("ActivityStorageVersion", 1));
}
