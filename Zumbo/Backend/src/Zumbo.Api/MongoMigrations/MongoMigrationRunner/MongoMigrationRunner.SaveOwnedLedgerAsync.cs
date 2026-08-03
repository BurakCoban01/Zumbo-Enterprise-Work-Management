using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

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
}
