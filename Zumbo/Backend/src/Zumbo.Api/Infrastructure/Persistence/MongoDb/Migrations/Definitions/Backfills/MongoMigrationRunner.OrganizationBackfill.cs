using MongoDB.Bson;
using MongoDB.Driver;

using Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Definitions.Backfills;

public sealed partial class MongoMigrationRunner
{
    private async Task<MongoMigrationOutcome> BackfillOrganizationVersionsAsync(
        CancellationToken cancellationToken)
    {
        return await new MongoOrganizationVersionBackfill(
                CreateExecutionContext(),
                OrganizationVersionMigrationId,
                OrganizationVersionChecksum)
            .ExecuteAsync(cancellationToken);
    }

    private static FilterDefinition<BsonDocument> OrganizationVersionFilter(BsonValue checkpoint) =>
        MongoOrganizationVersionBackfill.Filter(checkpoint);

    private static FilterDefinition<BsonDocument> OrganizationVersionForId(BsonValue id) =>
        MongoOrganizationVersionBackfill.ForId(id);
}
