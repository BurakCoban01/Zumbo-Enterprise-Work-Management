using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static async Task ReplaceMigratedActivityAsync(
        IMongoCollection<BsonDocument> collection,
        BsonDocument expected,
        CancellationToken cancellationToken)
    {
        var owner = Builders<BsonDocument>.Filter.Eq("_id", expected["_id"])
            & Builders<BsonDocument>.Filter.Eq("OrganizationId", expected["OrganizationId"])
            & Builders<BsonDocument>.Filter.Eq("ProjectId", expected["ProjectId"])
            & Builders<BsonDocument>.Filter.Eq("WorkItemId", expected["WorkItemId"]);
        try
        {
            await collection.ReplaceOneAsync(
                owner,
                expected,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new InvalidOperationException(
                $"Work item activity '{expected["_id"]}' conflicts with incompatible tenant ownership.",
                exception);
        }
    }
}
