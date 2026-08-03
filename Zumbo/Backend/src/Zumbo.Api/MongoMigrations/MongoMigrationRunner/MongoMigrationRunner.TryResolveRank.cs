using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    public static bool TryResolveRank(BsonValue createdAt, out long rank)
    {
        rank = 0;
        try
        {
            rank = createdAt.BsonType switch
            {
                BsonType.DateTime => DateTimeOffset.FromUnixTimeMilliseconds(createdAt.AsBsonDateTime.MillisecondsSinceEpoch).UtcTicks,
                BsonType.Int64 => createdAt.AsInt64,
                BsonType.Int32 => createdAt.AsInt32,
                BsonType.Array when createdAt.AsBsonArray.Count > 0 => NumericTicks(createdAt.AsBsonArray[0]),
                BsonType.Document => ResolveDocumentTicks(createdAt.AsBsonDocument),
                _ => 0
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentOutOfRangeException or FormatException)
        {
            rank = 0;
        }

        return rank > 0 && rank <= DateTimeOffset.MaxValue.UtcTicks;
    }
}
