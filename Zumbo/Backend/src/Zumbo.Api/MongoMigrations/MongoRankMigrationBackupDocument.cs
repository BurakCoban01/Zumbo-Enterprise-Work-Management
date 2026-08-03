using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed class MongoRankMigrationBackupDocument
{
    public string Id { get; set; } = string.Empty;
    public string MigrationId { get; set; } = string.Empty;
    public BsonValue DocumentId { get; set; } = BsonNull.Value;
    public bool HadRank { get; set; }
    public BsonValue PreviousRank { get; set; } = BsonNull.Value;
    public long AppliedRank { get; set; }
}
