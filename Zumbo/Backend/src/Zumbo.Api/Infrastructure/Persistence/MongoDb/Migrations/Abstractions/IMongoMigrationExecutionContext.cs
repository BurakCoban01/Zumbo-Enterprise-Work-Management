using MongoDB.Bson;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

namespace Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Abstractions;

internal interface IMongoMigrationExecutionContext
{
    IMongoDbService Mongo { get; }
    MongoMigrationOptions Options { get; }
    string Owner { get; }
    int BatchSize { get; }
    int MaxBatches { get; }

    bool TryResolveUtc(BsonValue value, out DateTime utc);
    long NumericTicks(BsonValue value);
    string? StringValue(BsonDocument document, string name);
    BsonArray ArrayValue(BsonDocument document, string name);

    Task<MongoMigrationLedgerDocument?> LoadLedgerAsync(string id, CancellationToken cancellationToken);
    void EnsureChecksum(MongoMigrationLedgerDocument ledger, string expected);
    Task<MongoMigrationLedgerDocument> GetOrCreateLedgerAsync(
        string migrationId,
        string checksum,
        CancellationToken cancellationToken);
    Task<MongoMigrationLedgerDocument> AcquireLeaseAsync(
        MongoMigrationLedgerDocument ledger,
        CancellationToken cancellationToken);
    Task SaveLedgerAsync(MongoMigrationLedgerDocument ledger, CancellationToken cancellationToken);
    Task SaveOwnedLedgerAsync(MongoMigrationLedgerDocument ledger, CancellationToken cancellationToken);
    Task SaveAndReleaseOwnedLedgerAsync(
        MongoMigrationLedgerDocument ledger,
        CancellationToken cancellationToken);
    MongoMigrationOutcome ToOutcome(MongoMigrationLedgerDocument ledger, string status);
}
