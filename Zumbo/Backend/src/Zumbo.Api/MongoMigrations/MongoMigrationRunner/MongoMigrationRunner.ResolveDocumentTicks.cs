using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static long ResolveDocumentTicks(BsonDocument document)
    {
        if (document.TryGetValue("Ticks", out var ticks)) return NumericTicks(ticks);
        if (document.TryGetValue("DateTime", out var dateTime) && dateTime.IsBsonDateTime)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(dateTime.AsBsonDateTime.MillisecondsSinceEpoch).UtcTicks;
        }

        return 0;
    }
}
