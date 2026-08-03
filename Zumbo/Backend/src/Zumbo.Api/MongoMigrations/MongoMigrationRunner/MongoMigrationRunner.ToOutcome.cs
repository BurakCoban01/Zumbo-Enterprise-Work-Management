using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static MongoMigrationOutcome ToOutcome(MongoMigrationLedgerDocument ledger, string status) =>
        new(ledger.Id, status, ledger.Examined, ledger.Changed, ledger.Skipped);
}
