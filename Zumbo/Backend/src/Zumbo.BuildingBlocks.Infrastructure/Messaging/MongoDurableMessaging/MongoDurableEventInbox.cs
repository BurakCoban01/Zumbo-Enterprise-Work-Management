using MongoDB.Driver;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

namespace Zumbo.BuildingBlocks.Infrastructure.Messaging;

public sealed class MongoDurableEventInbox(
    IMongoDbService mongo,
    MongoTransactionContext transactionContext) : IDurableEventInbox
{
    private readonly IMongoCollection<MongoInboxDocument> _messages =
        mongo.GetCollection<MongoInboxDocument>("inbox_messages", "WorkItems");

    public async Task<bool> HasProcessedAsync(
        string consumerName,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        var id = MongoInboxDocument.Key(consumerName, messageId);
        var filter = Builders<MongoInboxDocument>.Filter.Eq(x => x.Id, id);
        return transactionContext.Session is { } session
            ? await _messages.CountDocumentsAsync(session, filter, new CountOptions { Limit = 1 }, cancellationToken) > 0
            : await _messages.CountDocumentsAsync(filter, new CountOptions { Limit = 1 }, cancellationToken) > 0;
    }

    public async Task<bool> MarkProcessedAsync(
        string consumerName,
        string messageId,
        DateTimeOffset processedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var document = new MongoInboxDocument
        {
            Id = MongoInboxDocument.Key(consumerName, messageId),
            ConsumerName = consumerName.Trim(),
            MessageId = messageId.Trim(),
            ProcessedAtUtc = processedAtUtc.UtcDateTime
        };
        try
        {
            if (transactionContext.Session is { } session)
            {
                transactionContext.EnsureCompatible(_messages.Database.Client);
                await _messages.InsertOneAsync(session, document, cancellationToken: cancellationToken);
            }
            else
            {
                await _messages.InsertOneAsync(document, cancellationToken: cancellationToken);
            }

            return true;
        }
        catch (MongoWriteException exception) when (
            exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }
}
