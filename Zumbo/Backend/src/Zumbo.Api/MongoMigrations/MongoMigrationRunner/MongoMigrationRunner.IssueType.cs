using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static BsonDocument IssueType(
        string key,
        string name,
        string hierarchy,
        int position) => new()
        {
            ["Key"] = key,
            ["Name"] = name,
            ["Description"] = string.Empty,
            ["HierarchyLevel"] = hierarchy,
            ["Active"] = true,
            ["Position"] = position
        };
}
