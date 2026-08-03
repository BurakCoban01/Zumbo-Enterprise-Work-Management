using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private async Task<MongoMigrationLedgerDocument> GetOrCreateLedgerAsync(
        string migrationId,
        string checksum,
        CancellationToken cancellationToken)
    {
        var ledger = await LoadLedgerAsync(migrationId, cancellationToken);
        if (ledger is not null)
        {
            EnsureChecksum(ledger, checksum);
            return ledger;
        }

        var now = DateTime.UtcNow;
        ledger = new MongoMigrationLedgerDocument
        {
            Id = migrationId,
            Checksum = checksum,
            State = MongoMigrationStates.Running,
            StartedAt = now,
            UpdatedAt = now
        };
        try
        {
            await Ledgers.InsertOneAsync(ledger, cancellationToken: cancellationToken);
            return ledger;
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            ledger = await LoadLedgerAsync(migrationId, cancellationToken)
                ?? throw new InvalidOperationException("Migration ledger was concurrently created but cannot be loaded.");
            EnsureChecksum(ledger, checksum);
            return ledger;
        }
    }
}
