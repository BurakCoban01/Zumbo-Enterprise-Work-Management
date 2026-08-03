using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static string WorkItemRelationEdgeId(
        string projectId,
        string sourceWorkItemId,
        string targetWorkItemId,
        string relationType)
    {
        var value = Encoding.UTF8.GetBytes(
            $"{projectId}\n{sourceWorkItemId}\n{targetWorkItemId}\n{relationType}");
        return Convert.ToHexString(MD5.HashData(value)).ToLowerInvariant();
    }
}
