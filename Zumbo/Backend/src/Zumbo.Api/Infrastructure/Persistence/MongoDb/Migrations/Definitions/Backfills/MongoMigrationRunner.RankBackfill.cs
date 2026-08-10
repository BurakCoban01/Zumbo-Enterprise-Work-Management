using MongoDB.Bson;
using MongoDB.Driver;

using Zumbo.Api.Infrastructure.Persistence.MongoDb.Migrations.Definitions.Backfills;

public sealed partial class MongoMigrationRunner
{
    private async Task<MongoMigrationOutcome> BackfillRanksAsync(CancellationToken cancellationToken)
    {
        return await new MongoRankBackfill(
                CreateExecutionContext(),
                RankMigrationId,
                RankChecksum)
            .ExecuteAsync(cancellationToken);
    }

    private static FilterDefinition<BsonDocument> RankCandidateFilter(BsonValue checkpoint) =>
        MongoRankBackfill.RankCandidateFilter(checkpoint);

    private static FilterDefinition<BsonDocument> RankCandidateForId(BsonValue id) =>
        MongoRankBackfill.RankCandidateForId(id);

    public static bool TryResolveRank(BsonValue createdAt, out long rank) =>
        MongoRankBackfill.TryResolveRank(createdAt, NumericTicks, out rank);

    private static long ResolveDocumentTicks(BsonDocument document) =>
        MongoRankBackfill.ResolveDocumentTicks(document, NumericTicks);
}
