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

    private static string? StringValue(BsonDocument document, string name)
    {
        var value = document.GetValue(name, BsonNull.Value);
        return value.IsString ? value.AsString : null;
    }

    private static long NumericTicks(BsonValue value) => value.BsonType switch
    {
        BsonType.Int64 => value.AsInt64,
        BsonType.Int32 => value.AsInt32,
        _ => 0
    };

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
