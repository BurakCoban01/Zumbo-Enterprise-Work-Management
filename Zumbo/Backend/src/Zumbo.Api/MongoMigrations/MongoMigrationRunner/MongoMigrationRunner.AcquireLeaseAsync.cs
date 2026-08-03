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
}
