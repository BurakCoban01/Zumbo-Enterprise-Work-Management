using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static string SerializeIndex(MongoIndexSpecification index) =>
        $"{index.Module}:{index.Collection}:{index.Name}:{index.Keys}:{index.Unique}:{index.CaseInsensitive}:{index.ExpireAfter}:{index.PartialFilter}";
}
