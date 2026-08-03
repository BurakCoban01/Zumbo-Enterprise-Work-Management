using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static void AddProjectDefault(
        BsonDocument document,
        ICollection<UpdateDefinition<BsonDocument>> updates,
        string field,
        BsonValue value)
    {
        if (!document.Contains(field) || document[field].IsBsonNull)
        {
            updates.Add(Builders<BsonDocument>.Update.Set(field, value));
        }
    }
}
