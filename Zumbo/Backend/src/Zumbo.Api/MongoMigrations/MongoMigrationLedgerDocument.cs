using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed class MongoMigrationLedgerDocument
{
    public string Id { get; set; } = string.Empty;
    public string Checksum { get; set; } = string.Empty;
    public string State { get; set; } = MongoMigrationStates.Running;
    public BsonValue Checkpoint { get; set; } = BsonNull.Value;
    public BsonValue RollbackCheckpoint { get; set; } = BsonNull.Value;
    public long Examined { get; set; }
    public long Changed { get; set; }
    public long Skipped { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? RolledBackAt { get; set; }
}
