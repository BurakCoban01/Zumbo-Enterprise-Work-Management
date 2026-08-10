using MongoDB.Bson;
using MongoDB.Driver;

using Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Definitions.Backfills;

public sealed partial class MongoMigrationRunner
{
    private async Task<MongoMigrationOutcome> ExpireLegacyTeamInvitesAsync(
        CancellationToken cancellationToken)
    {
        return await new MongoTeamInviteBackfill(
                CreateExecutionContext(),
                TeamInviteTokenMigrationId,
                TeamInviteTokenChecksum)
            .ExecuteAsync(cancellationToken);
    }

    private static FilterDefinition<BsonDocument> LegacyTeamInviteFilter(BsonValue checkpoint) =>
        MongoTeamInviteBackfill.Filter(checkpoint);

    private static FilterDefinition<BsonDocument> TeamVersionForId(BsonValue id, long version) =>
        MongoTeamInviteBackfill.ForId(id, version);
}
