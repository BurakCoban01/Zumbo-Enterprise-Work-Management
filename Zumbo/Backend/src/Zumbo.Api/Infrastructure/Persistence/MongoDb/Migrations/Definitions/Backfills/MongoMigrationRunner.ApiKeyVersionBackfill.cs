using MongoDB.Bson;
using MongoDB.Driver;

public sealed partial class MongoMigrationRunner
{
    private async Task<MongoMigrationOutcome> BackfillApiKeyVersionsAsync(CancellationToken cancellationToken)
    {
        return await new MongoApiKeyVersionBackfill(
                CreateExecutionContext(),
                ApiKeyVersionMigrationId,
                ApiKeyVersionChecksum)
            .ExecuteAsync(cancellationToken);
    }

    private static FilterDefinition<BsonDocument> ApiKeyVersionFilter(BsonValue checkpoint) =>
        MongoApiKeyVersionBackfill.Filter(checkpoint);

    private static FilterDefinition<BsonDocument> ApiKeyVersionForId(BsonValue id) =>
        MongoApiKeyVersionBackfill.ForId(id);
}
