using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private async Task SaveLedgerAsync(MongoMigrationLedgerDocument ledger, CancellationToken cancellationToken)
    {
        ledger.UpdatedAt = DateTime.UtcNow;
        var result = await Ledgers.ReplaceOneAsync(x => x.Id == ledger.Id, ledger, cancellationToken: cancellationToken);
        if (result.MatchedCount != 1)
        {
            throw new InvalidOperationException($"Migration ledger '{ledger.Id}' disappeared.");
        }
    }
}
