using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static BsonArray ArrayValue(BsonDocument document, string name)
    {
        var value = document.GetValue(name, new BsonArray());
        return value.IsBsonArray ? value.AsBsonArray : new BsonArray();
    }
}
