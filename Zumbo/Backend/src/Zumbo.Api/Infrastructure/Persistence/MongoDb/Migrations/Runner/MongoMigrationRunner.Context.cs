using MongoDB.Bson;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

using Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Abstractions;

public sealed partial class MongoMigrationRunner
{
    private IMongoMigrationExecutionContext CreateExecutionContext() =>
        new MongoMigrationExecutionContext(this, mongo);

    private sealed class MongoMigrationExecutionContext(
        MongoMigrationRunner runner,
        IMongoDbService mongo)
        : IMongoMigrationExecutionContext
    {
        public IMongoDbService Mongo => mongo;
        public MongoMigrationOptions Options => runner._options;
        public string Owner => runner._owner;
        public int BatchSize => runner.BatchSize;
        public int MaxBatches => runner.MaxBatches;

        public bool TryResolveUtc(BsonValue value, out DateTime utc) =>
            MongoMigrationRunner.TryResolveUtc(value, out utc);

        public long NumericTicks(BsonValue value) => MongoMigrationRunner.NumericTicks(value);

        public string? StringValue(BsonDocument document, string name) =>
            MongoMigrationRunner.StringValue(document, name);

        public BsonArray ArrayValue(BsonDocument document, string name) =>
            MongoMigrationRunner.ArrayValue(document, name);

        public Task<MongoMigrationLedgerDocument?> LoadLedgerAsync(
            string id,
            CancellationToken cancellationToken) =>
            runner.LoadLedgerAsync(id, cancellationToken);

        public void EnsureChecksum(MongoMigrationLedgerDocument ledger, string expected) =>
            MongoMigrationRunner.EnsureChecksum(ledger, expected);

        public Task<MongoMigrationLedgerDocument> GetOrCreateLedgerAsync(
            string migrationId,
            string checksum,
            CancellationToken cancellationToken) =>
            runner.GetOrCreateLedgerAsync(migrationId, checksum, cancellationToken);

        public Task<MongoMigrationLedgerDocument> AcquireLeaseAsync(
            MongoMigrationLedgerDocument ledger,
            CancellationToken cancellationToken) =>
            runner.AcquireLeaseAsync(ledger, cancellationToken);

        public Task SaveLedgerAsync(
            MongoMigrationLedgerDocument ledger,
            CancellationToken cancellationToken) =>
            runner.SaveLedgerAsync(ledger, cancellationToken);

        public Task SaveOwnedLedgerAsync(
            MongoMigrationLedgerDocument ledger,
            CancellationToken cancellationToken) =>
            runner.SaveOwnedLedgerAsync(ledger, cancellationToken);

        public Task SaveAndReleaseOwnedLedgerAsync(
            MongoMigrationLedgerDocument ledger,
            CancellationToken cancellationToken) =>
            runner.SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);

        public MongoMigrationOutcome ToOutcome(MongoMigrationLedgerDocument ledger, string status) =>
            MongoMigrationRunner.ToOutcome(ledger, status);
    }
}
