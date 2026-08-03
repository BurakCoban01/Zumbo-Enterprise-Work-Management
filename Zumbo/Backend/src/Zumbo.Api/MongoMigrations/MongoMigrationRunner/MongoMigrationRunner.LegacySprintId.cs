using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static string LegacySprintId(string projectId, string sprintId)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(projectId + ":" + sprintId));
        return "legacy-" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
