using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static string? StringValue(BsonDocument document, string name)
    {
        var value = document.GetValue(name, BsonNull.Value);
        return value.IsString ? value.AsString : null;
    }
}
