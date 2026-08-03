using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private async Task<MongoMigrationLedgerDocument?> LoadLedgerAsync(string id, CancellationToken cancellationToken) =>
        (MongoMigrationLedgerDocument?)await Ledgers.Find(x => x.Id == id).FirstOrDefaultAsync(cancellationToken);
}
