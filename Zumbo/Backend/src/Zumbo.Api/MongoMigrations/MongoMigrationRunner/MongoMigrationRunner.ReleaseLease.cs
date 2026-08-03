using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static void ReleaseLease(MongoMigrationLedgerDocument ledger)
    {
        ledger.LeaseOwner = null;
        ledger.LeaseExpiresAt = null;
    }
}
