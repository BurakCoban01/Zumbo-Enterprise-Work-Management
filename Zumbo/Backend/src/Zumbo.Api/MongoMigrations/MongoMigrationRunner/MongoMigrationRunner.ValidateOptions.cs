using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private void ValidateOptions()
    {
        if (_options.BatchSize <= 0) throw new InvalidOperationException("MongoMigrations:BatchSize must be positive.");
        if (_options.MaxBatchesPerRun <= 0) throw new InvalidOperationException("MongoMigrations:MaxBatchesPerRun must be positive.");
        if (_options.RunDataMigrations && !_options.DryRun && _options.BatchSize > 10_000)
        {
            logger.LogWarning("Mongo migration batch size was capped at 10000 from {ConfiguredBatchSize}", _options.BatchSize);
        }
    }
}
