using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static bool TryResolveUtc(BsonValue value, out DateTime utc)
    {
        utc = default;
        try
        {
            utc = value.BsonType switch
            {
                BsonType.DateTime => value.ToUniversalTime(),
                BsonType.Int64 => new DateTime(value.AsInt64, DateTimeKind.Utc),
                BsonType.Int32 => new DateTime(value.AsInt32, DateTimeKind.Utc),
                BsonType.Array when value.AsBsonArray.Count > 0 =>
                    new DateTime(NumericTicks(value.AsBsonArray[0]), DateTimeKind.Utc),
                BsonType.Document when value.AsBsonDocument.TryGetValue("Ticks", out var ticks) =>
                    new DateTime(NumericTicks(ticks), DateTimeKind.Utc),
                _ => default
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentOutOfRangeException)
        {
            utc = default;
        }

        return utc != default;
    }
}
