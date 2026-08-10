using MongoDB.Bson;
using MongoDB.Driver;

using Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Definitions.Backfills;

public sealed partial class MongoMigrationRunner
{
    private Task<MongoMigrationOutcome> BackfillUserVersionsAsync(CancellationToken cancellationToken) =>
        new MongoUserVersionBackfill(
            CreateExecutionContext(),
            UserVersionMigrationId,
            UserVersionChecksum)
            .ExecuteAsync(cancellationToken);

    private static FilterDefinition<BsonDocument> UserVersionFilter(BsonValue checkpoint) =>
        MongoUserVersionBackfill.UserVersionFilter(checkpoint);

    private static FilterDefinition<BsonDocument> UserVersionForId(BsonValue id) =>
        MongoUserVersionBackfill.UserVersionForId(id);
}
