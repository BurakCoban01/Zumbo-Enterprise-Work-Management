using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static long NumericTicks(BsonValue value) => value.BsonType switch
    {
        BsonType.Int64 => value.AsInt64,
        BsonType.Int32 => value.AsInt32,
        _ => 0
    };
}
