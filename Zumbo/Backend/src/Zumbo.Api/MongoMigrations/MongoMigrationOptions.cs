using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed class MongoMigrationOptions
{
    public bool DryRun { get; init; }
    public bool RunDataMigrations { get; init; }
    public int BatchSize { get; init; } = 100;
    public int MaxBatchesPerRun { get; init; } = 20;
    public string? RollbackMigrationId { get; init; }
}
