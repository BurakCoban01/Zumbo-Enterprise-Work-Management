using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private async Task<MongoMigrationLedgerDocument> AcquireLeaseAsync(
        MongoMigrationLedgerDocument ledger,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var filter = Builders<MongoMigrationLedgerDocument>.Filter.Eq(x => x.Id, ledger.Id)
            & Builders<MongoMigrationLedgerDocument>.Filter.Or(
                Builders<MongoMigrationLedgerDocument>.Filter.Eq(x => x.LeaseOwner, _owner),
                Builders<MongoMigrationLedgerDocument>.Filter.Eq(x => x.LeaseOwner, null),
                Builders<MongoMigrationLedgerDocument>.Filter.Lt(x => x.LeaseExpiresAt, now));
        var update = Builders<MongoMigrationLedgerDocument>.Update
            .Set(x => x.LeaseOwner, _owner)
            .Set(x => x.LeaseExpiresAt, now.Add(LeaseDuration))
            .Set(x => x.UpdatedAt, now);
        return await Ledgers.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<MongoMigrationLedgerDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken) ?? ledger;
    }

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

    private async Task<MongoMigrationLedgerDocument?> LoadLedgerAsync(string id, CancellationToken cancellationToken) =>
        (MongoMigrationLedgerDocument?)await Ledgers.Find(x => x.Id == id).FirstOrDefaultAsync(cancellationToken);

    private async Task SaveLedgerAsync(MongoMigrationLedgerDocument ledger, CancellationToken cancellationToken)
    {
        ledger.UpdatedAt = DateTime.UtcNow;
        var result = await Ledgers.ReplaceOneAsync(x => x.Id == ledger.Id, ledger, cancellationToken: cancellationToken);
        if (result.MatchedCount != 1)
        {
            throw new InvalidOperationException($"Migration ledger '{ledger.Id}' disappeared.");
        }
    }

    private async Task SaveOwnedLedgerAsync(MongoMigrationLedgerDocument ledger, CancellationToken cancellationToken)
    {
        ledger.UpdatedAt = DateTime.UtcNow;
        ledger.LeaseExpiresAt = ledger.UpdatedAt.Add(LeaseDuration);
        var result = await Ledgers.ReplaceOneAsync(
            x => x.Id == ledger.Id && x.LeaseOwner == _owner,
            ledger,
            cancellationToken: cancellationToken);
        if (result.ModifiedCount != 1)
        {
            throw new InvalidOperationException($"Migration lease for '{ledger.Id}' was lost.");
        }
    }

    private async Task SaveAndReleaseOwnedLedgerAsync(
        MongoMigrationLedgerDocument ledger,
        CancellationToken cancellationToken)
    {
        ledger.UpdatedAt = DateTime.UtcNow;
        ReleaseLease(ledger);
        var result = await Ledgers.ReplaceOneAsync(
            x => x.Id == ledger.Id && x.LeaseOwner == _owner,
            ledger,
            cancellationToken: cancellationToken);
        if (result.ModifiedCount != 1)
        {
            throw new InvalidOperationException($"Migration lease for '{ledger.Id}' was lost.");
        }
    }

    private static void ReleaseLease(MongoMigrationLedgerDocument ledger)
    {
        ledger.LeaseOwner = null;
        ledger.LeaseExpiresAt = null;
    }

    private static void EnsureChecksum(MongoMigrationLedgerDocument ledger, string expected)
    {
        if (!string.Equals(ledger.Checksum, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Migration '{ledger.Id}' checksum changed after it was recorded.");
        }
    }

    private static string Checksum(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static MongoMigrationOutcome ToOutcome(MongoMigrationLedgerDocument ledger, string status) =>
        new(ledger.Id, status, ledger.Examined, ledger.Changed, ledger.Skipped);
}
