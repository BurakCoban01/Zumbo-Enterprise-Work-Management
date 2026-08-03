using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static FilterDefinition<BsonDocument> WorkItemActivityFilter(BsonValue checkpoint)
    {
        var filter = WorkItemActivityVersionFilter();
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }
        return filter;
    }
}
