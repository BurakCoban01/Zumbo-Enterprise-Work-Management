using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static void EnsureChecksum(MongoMigrationLedgerDocument ledger, string expected)
    {
        if (!string.Equals(ledger.Checksum, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Migration '{ledger.Id}' checksum changed after it was recorded.");
        }
    }
}
