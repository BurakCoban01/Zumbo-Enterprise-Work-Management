using MongoDB.Driver;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

namespace Zumbo.BuildingBlocks.Infrastructure.Messaging;

public sealed class MongoDurableEventOutbox(
    IMongoDbService mongo,
    MongoTransactionContext transactionContext) : IDurableEventOutbox
{
    private const string OwnerModule = "WorkItems";
    private readonly IMongoCollection<MongoOutboxDocument> _messages =
        mongo.GetCollection<MongoOutboxDocument>("outbox_messages", OwnerModule);

    public async Task EnqueueAsync(
        DurableEventEnvelope message,
        CancellationToken cancellationToken = default)
    {
        var session = transactionContext.Session;
        if (session is null || !session.IsInTransaction)
        {
            throw new InvalidOperationException(
                "Durable events must be enqueued inside an active MongoDB transaction.");
        }

        transactionContext.EnsureCompatible(_messages.Database.Client);
        try
        {
            await _messages.InsertOneAsync(
                session,
                MongoOutboxDocument.From(message),
                cancellationToken: cancellationToken);
        }
        catch (MongoWriteException exception) when (
            exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // A repeated envelope or deduplication key represents the same logical event.
        }
    }

    public async Task<IReadOnlyList<DurableEventLease>> ClaimAsync(
        string workerId,
        int batchSize,
        TimeSpan leaseDuration,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateClaim(workerId, batchSize, leaseDuration);
        var result = new List<DurableEventLease>(batchSize);
        var now = Utc(nowUtc);
        for (var index = 0; index < batchSize; index++)
        {
            var leaseToken = Guid.NewGuid().ToString("N");
            var eligible = Builders<MongoOutboxDocument>.Filter.Or(
                Builders<MongoOutboxDocument>.Filter.And(
                    Builders<MongoOutboxDocument>.Filter.Eq(x => x.Status, DurableMessageStates.Pending),
                    Builders<MongoOutboxDocument>.Filter.Lte(x => x.AvailableAtUtc, now)),
                Builders<MongoOutboxDocument>.Filter.And(
                    Builders<MongoOutboxDocument>.Filter.Eq(x => x.Status, DurableMessageStates.Processing),
                    Builders<MongoOutboxDocument>.Filter.Lte(x => x.LeaseUntilUtc, now)));
            var update = Builders<MongoOutboxDocument>.Update
                .Set(x => x.Status, DurableMessageStates.Processing)
                .Inc(x => x.AttemptCount, 1)
                .Set(x => x.LeaseOwner, workerId.Trim())
                .Set(x => x.LeaseToken, leaseToken)
                .Set(x => x.LeaseUntilUtc, Utc(nowUtc.Add(leaseDuration)))
                .Set(x => x.UpdatedAtUtc, now);
            var claimed = await _messages.FindOneAndUpdateAsync(
                eligible,
                update,
                new FindOneAndUpdateOptions<MongoOutboxDocument>
                {
                    ReturnDocument = ReturnDocument.After,
                    Sort = Builders<MongoOutboxDocument>.Sort
                        .Ascending(x => x.OccurredAtUtc)
                        .Ascending(x => x.Id)
                },
                cancellationToken);
            if (claimed is null)
            {
                break;
            }

            result.Add(claimed.ToLease());
        }

        return result;
    }

    public async Task<bool> CompleteAsync(
        string messageId,
        string leaseToken,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var filter = OwnedLease(messageId, leaseToken);
        var now = Utc(completedAtUtc);
        var update = Builders<MongoOutboxDocument>.Update
            .Set(x => x.Status, DurableMessageStates.Completed)
            .Set(x => x.CompletedAtUtc, now)
            .Set(x => x.UpdatedAtUtc, now)
            .Set(x => x.LeaseOwner, null)
            .Set(x => x.LeaseToken, null)
            .Set(x => x.LeaseUntilUtc, null)
            .Set(x => x.LastError, null);
        return (await _messages.UpdateOneAsync(filter, update, cancellationToken: cancellationToken)).ModifiedCount == 1;
    }

    public async Task<DurableMessageFailure> FailAsync(
        string messageId,
        string leaseToken,
        string error,
        int maximumAttempts,
        DateTimeOffset nowUtc,
        DateTimeOffset nextAttemptAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (maximumAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        }

        var now = Utc(nowUtc);
        var baseFilter = OwnedLease(messageId, leaseToken);
        var deadFilter = baseFilter & Builders<MongoOutboxDocument>.Filter.Gte(x => x.AttemptCount, maximumAttempts);
        var deadUpdate = Builders<MongoOutboxDocument>.Update
            .Set(x => x.Status, DurableMessageStates.DeadLetter)
            .Set(x => x.DeadLetteredAtUtc, now)
            .Set(x => x.UpdatedAtUtc, now)
            .Set(x => x.LeaseOwner, null)
            .Set(x => x.LeaseToken, null)
            .Set(x => x.LeaseUntilUtc, null)
            .Set(x => x.LastError, Truncate(error, 4000));
        var dead = await _messages.FindOneAndUpdateAsync(
            deadFilter,
            deadUpdate,
            new FindOneAndUpdateOptions<MongoOutboxDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
        if (dead is not null)
        {
            return new DurableMessageFailure(true, true, dead.AttemptCount, null);
        }

        var retryUpdate = Builders<MongoOutboxDocument>.Update
            .Set(x => x.Status, DurableMessageStates.Pending)
            .Set(x => x.AvailableAtUtc, Utc(nextAttemptAtUtc))
            .Set(x => x.UpdatedAtUtc, now)
            .Set(x => x.LeaseOwner, null)
            .Set(x => x.LeaseToken, null)
            .Set(x => x.LeaseUntilUtc, null)
            .Set(x => x.LastError, Truncate(error, 4000));
        var retry = await _messages.FindOneAndUpdateAsync(
            baseFilter,
            retryUpdate,
            new FindOneAndUpdateOptions<MongoOutboxDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
        return retry is null
            ? new DurableMessageFailure(false, false, 0, null)
            : new DurableMessageFailure(true, false, retry.AttemptCount, nextAttemptAtUtc);
    }

    public async Task<bool> ReplayDeadLetterAsync(
        string messageId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var now = Utc(nowUtc);
        var filter = Builders<MongoOutboxDocument>.Filter.Eq(x => x.Id, Required(messageId))
            & Builders<MongoOutboxDocument>.Filter.Eq(x => x.Status, DurableMessageStates.DeadLetter);
        var update = Builders<MongoOutboxDocument>.Update
            .Set(x => x.Status, DurableMessageStates.Pending)
            .Set(x => x.AttemptCount, 0)
            .Set(x => x.AvailableAtUtc, now)
            .Set(x => x.UpdatedAtUtc, now)
            .Set(x => x.LeaseOwner, null)
            .Set(x => x.LeaseToken, null)
            .Set(x => x.LeaseUntilUtc, null)
            .Set(x => x.LastError, null)
            .Set(x => x.DeadLetteredAtUtc, null)
            .Set(x => x.CompletedAtUtc, null);
        return (await _messages.UpdateOneAsync(filter, update, cancellationToken: cancellationToken)).ModifiedCount == 1;
    }

    public async Task<IReadOnlyList<DurableDeadLetterSummary>> ListDeadLettersAsync(
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (pageSize is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        var filter = Builders<MongoOutboxDocument>.Filter.Eq(
            x => x.Status,
            DurableMessageStates.DeadLetter);
        var documents = await _messages.Find(filter)
            .SortByDescending(x => x.DeadLetteredAtUtc)
            .ThenBy(x => x.Id)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);
        return documents.Select(x => new DurableDeadLetterSummary(
            x.Id,
            x.EventType,
            x.AttemptCount,
            Offset(x.DeadLetteredAtUtc ?? x.OccurredAtUtc))).ToList();
    }

    public async Task<DurableOutboxMetrics> GetMetricsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var pendingFilter = Builders<MongoOutboxDocument>.Filter.Eq(x => x.Status, DurableMessageStates.Pending);
        var processingFilter = Builders<MongoOutboxDocument>.Filter.Eq(x => x.Status, DurableMessageStates.Processing);
        var deadFilter = Builders<MongoOutboxDocument>.Filter.Eq(x => x.Status, DurableMessageStates.DeadLetter);
        var completedFilter = Builders<MongoOutboxDocument>.Filter.Eq(x => x.Status, DurableMessageStates.Completed);
        var retriedFilter = Builders<MongoOutboxDocument>.Filter.Gt(x => x.AttemptCount, 1);
        var pending = await _messages.CountDocumentsAsync(pendingFilter, cancellationToken: cancellationToken);
        var processing = await _messages.CountDocumentsAsync(processingFilter, cancellationToken: cancellationToken);
        var dead = await _messages.CountDocumentsAsync(deadFilter, cancellationToken: cancellationToken);
        var completed = await _messages.CountDocumentsAsync(completedFilter, cancellationToken: cancellationToken);
        var retried = await _messages.CountDocumentsAsync(retriedFilter, cancellationToken: cancellationToken);
        var oldest = await _messages.Find(pendingFilter)
            .SortBy(x => x.OccurredAtUtc)
            .Project(x => (DateTime?)x.OccurredAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return new DurableOutboxMetrics(
            pending,
            processing,
            dead,
            completed,
            retried,
            oldest is null ? null : Offset(oldest.Value),
            nowUtc);
    }

    private static FilterDefinition<MongoOutboxDocument> OwnedLease(string messageId, string leaseToken) =>
        Builders<MongoOutboxDocument>.Filter.Eq(x => x.Id, Required(messageId))
        & Builders<MongoOutboxDocument>.Filter.Eq(x => x.Status, DurableMessageStates.Processing)
        & Builders<MongoOutboxDocument>.Filter.Eq(x => x.LeaseToken, Required(leaseToken));

    private static void ValidateClaim(string workerId, int batchSize, TimeSpan leaseDuration)
    {
        _ = Required(workerId);
        if (batchSize is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromMinutes(15))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
    }

    private static string Required(string value) =>
        !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ArgumentException("Value cannot be empty.");

    private static string Truncate(string value, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "Unknown durable event failure." : value.Trim();
        return normalized[..Math.Min(normalized.Length, maximumLength)];
    }

    private static DateTime Utc(DateTimeOffset value) => value.UtcDateTime;
    private static DateTimeOffset Offset(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
