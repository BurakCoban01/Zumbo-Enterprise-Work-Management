using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static FilterDefinition<BsonDocument> SprintLifecycleFilter(BsonValue checkpoint)
    {
        var filter = Builders<BsonDocument>.Filter.Exists("SprintId", true)
            & Builders<BsonDocument>.Filter.Ne("SprintId", BsonNull.Value)
            & Builders<BsonDocument>.Filter.Ne("SprintId", string.Empty)
            & Builders<BsonDocument>.Filter.Exists("SprintLifecycleMigratedBy", false);
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return filter;
    }
}
