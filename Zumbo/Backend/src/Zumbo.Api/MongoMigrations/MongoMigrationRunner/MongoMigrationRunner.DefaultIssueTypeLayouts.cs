using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static BsonArray DefaultIssueTypeLayouts() => new(
        DefaultIssueTypeKeys.Select(key => new BsonDocument
        {
            ["IssueTypeKey"] = key,
            ["FieldKeys"] = new BsonArray()
        }));
}
