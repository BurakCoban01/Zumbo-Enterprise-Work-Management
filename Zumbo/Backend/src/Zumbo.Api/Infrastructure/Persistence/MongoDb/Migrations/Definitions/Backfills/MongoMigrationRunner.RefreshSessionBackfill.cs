using MongoDB.Bson;
using MongoDB.Driver;

public sealed partial class MongoMigrationRunner
{
    private async Task<MongoMigrationOutcome> BackfillRefreshSessionsAsync(
        CancellationToken cancellationToken)
    {
        return await new MongoRefreshSessionBackfill(
                CreateExecutionContext(),
                RefreshSessionMigrationId,
                RefreshSessionChecksum)
            .ExecuteAsync(cancellationToken);
    }

    private static FilterDefinition<BsonDocument> RefreshSessionUserFilter(BsonValue checkpoint) =>
        MongoRefreshSessionBackfill.RefreshSessionUserFilter(checkpoint);

    private static bool TryCreateRefreshSession(
        BsonValue value,
        string userId,
        string organizationId,
        out BsonDocument session) =>
        MongoRefreshSessionBackfill.TryCreateRefreshSession(
            value,
            userId,
            organizationId,
            StringValue,
            static current => TryResolveUtc(current, out var utc) ? utc : null,
            out session);

    private static async Task EnsureRefreshSessionMatchesAsync(
        IMongoCollection<BsonDocument> sessions,
        BsonDocument expected,
        CancellationToken cancellationToken) =>
        await MongoRefreshSessionBackfill.EnsureRefreshSessionMatchesAsync(
            sessions,
            expected,
            cancellationToken);
}
