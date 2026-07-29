using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Infrastructure.Messaging;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Audit;
using Zumbo.RepositoryContracts;

namespace Zumbo.PersistenceIntegrationTests;

public sealed class MongoDurableMessagingTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
    private readonly MongoDbService _mongo;
    private readonly MongoTransactionContext _context = new();
    private readonly IMongoDatabase _defaultDatabase;
    private readonly IMongoDatabase _database;
    private readonly IMongoDatabase _auditDatabase;
    private readonly Zumbo.BuildingBlocks.Application.Persistence.IDocumentRepository<RepositoryContractDocument> _repository;
    private readonly MongoDurableEventOutbox _outbox;
    private readonly MongoDurableEventInbox _inbox;
    private readonly MongoDurableTransactionRunner _transactions;

    public MongoDurableMessagingTests()
    {
        var connectionString = Environment.GetEnvironmentVariable("ZUMBO_TEST_MONGO_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ZUMBO_TEST_MONGO_CONNECTION_STRING is required for real Mongo durable messaging tests.");
        }

        var databaseName = $"ZumboData005_{Guid.NewGuid():N}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = connectionString,
                ["MongoDb:DatabaseName"] = databaseName,
                ["Modules:WorkItems:MongoDb:DatabaseName"] = databaseName + "_WorkItems",
                ["Modules:Audit:MongoDb:DatabaseName"] = databaseName + "_Audit"
            })
            .Build();
        _mongo = new MongoDbService(configuration);
        _defaultDatabase = _mongo.GetDatabase("Default");
        _database = _mongo.GetDatabase("WorkItems");
        _auditDatabase = _mongo.GetDatabase("Audit");
        _repository = new MongoRepository<RepositoryContractDocument>(_mongo, _context);
        _outbox = new MongoDurableEventOutbox(_mongo, _context);
        _inbox = new MongoDurableEventInbox(_mongo, _context);
        _transactions = new MongoDurableTransactionRunner(_mongo, _context);
    }

    public async Task InitializeAsync()
    {
        await _database.CreateCollectionAsync("repositorycontracts");
        await _database.CreateCollectionAsync("outbox_messages");
        await _database.CreateCollectionAsync("inbox_messages");
        await _auditDatabase.CreateCollectionAsync("auditlogs");
        var outbox = _database.GetCollection<MongoOutboxDocument>("outbox_messages");
        await outbox.Indexes.CreateOneAsync(new CreateIndexModel<MongoOutboxDocument>(
            Builders<MongoOutboxDocument>.IndexKeys
                .Ascending(x => x.OwnerModule)
                .Ascending(x => x.EventType)
                .Ascending(x => x.DeduplicationKey),
            new CreateIndexOptions<MongoOutboxDocument>
            {
                Name = "ux_outbox_owner_event_deduplication",
                Unique = true,
                PartialFilterExpression = Builders<MongoOutboxDocument>.Filter.Type(
                    x => x.DeduplicationKey,
                    MongoDB.Bson.BsonType.String)
            }));
        var auditLogs = _auditDatabase.GetCollection<AuditLogDocument>("auditlogs");
        await auditLogs.Indexes.CreateOneAsync(new CreateIndexModel<AuditLogDocument>(
            Builders<AuditLogDocument>.IndexKeys.Ascending(x => x.DeduplicationKey),
            new CreateIndexOptions<AuditLogDocument>
            {
                Name = "ux_auditlogs_deduplication_key",
                Unique = true,
                PartialFilterExpression = Builders<AuditLogDocument>.Filter.Type(
                    x => x.DeduplicationKey,
                    MongoDB.Bson.BsonType.String)
            }));
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _defaultDatabase.Client.DropDatabaseAsync(_defaultDatabase.DatabaseNamespace.DatabaseName);
        await _database.Client.DropDatabaseAsync(_database.DatabaseNamespace.DatabaseName);
        await _auditDatabase.Client.DropDatabaseAsync(_auditDatabase.DatabaseNamespace.DatabaseName);
    }

    [Fact]
    public async Task BusinessWriteAndOutbox_CommitAndRollbackAtomically()
    {
        await _transactions.ExecuteAsync("WorkItems", async token =>
        {
            await _repository.CreateAsync(new RepositoryContractDocument { Id = "committed", Name = "committed" }, token);
            await _outbox.EnqueueAsync(Event("commit-event"), token);
        });

        Assert.True(await _repository.ExistsByFilterAsync(x => x.Id == "committed"));
        Assert.Equal(1, (await _outbox.GetMetricsAsync(Now)).Pending);

        await Assert.ThrowsAsync<IntentionalFailure>(() =>
            _transactions.ExecuteAsync("WorkItems", async token =>
            {
                await _repository.CreateAsync(new RepositoryContractDocument { Id = "rolled-back", Name = "rolled-back" }, token);
                await _outbox.EnqueueAsync(Event("rollback-event"), token);
                throw new IntentionalFailure();
            }));

        Assert.False(await _repository.ExistsByFilterAsync(x => x.Id == "rolled-back"));
        Assert.Equal(1, (await _outbox.GetMetricsAsync(Now)).Pending);
    }

    [Fact]
    public async Task CallerCancellation_RollsBackBusinessWriteAndOutbox()
    {
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _transactions.ExecuteAsync(
                "WorkItems",
                async token =>
                {
                    await _repository.CreateAsync(
                        new RepositoryContractDocument
                        {
                            Id = "cancelled-write",
                            Name = "cancelled-write"
                        },
                        token);
                    await _outbox.EnqueueAsync(Event("cancelled-event"), token);
                    await cancellation.CancelAsync();
                    token.ThrowIfCancellationRequested();
                },
                cancellation.Token));

        Assert.False(await _repository.ExistsByFilterAsync(x => x.Id == "cancelled-write"));
        Assert.Equal(0, (await _outbox.GetMetricsAsync(Now)).Pending);
    }

    [Fact]
    public async Task ConcurrentTransactions_RetryTransientWriteConflictWithoutLosingAnUpdate()
    {
        const string id = "transient-retry-counter";
        await _repository.CreateAsync(new RepositoryContractDocument { Id = id, Value = 0 });
        var contexts = new[] { new MongoTransactionContext(), new MongoTransactionContext() };
        var runners = contexts.Select(context => new MongoDurableTransactionRunner(_mongo, context)).ToArray();
        var repositories = contexts.Select(context =>
            (Zumbo.BuildingBlocks.Application.Persistence.IDocumentRepository<RepositoryContractDocument>)
            new MongoRepository<RepositoryContractDocument>(_mongo, context)).ToArray();
        var firstReads = Enumerable.Range(0, 2).Select(_ =>
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var attempts = new int[2];

        try
        {
            await Task.WhenAll(Enumerable.Range(0, 2).Select(index => runners[index].ExecuteAsync(
                "Default",
                async token =>
                {
                    var document = await repositories[index].SelectAsync(x => x.Id == id, token)
                        ?? throw new InvalidOperationException("Counter document disappeared.");
                    if (Interlocked.Increment(ref attempts[index]) == 1)
                    {
                        firstReads[index].SetResult();
                        await firstReads[1 - index].Task.WaitAsync(token);
                    }

                    document.Value++;
                    await repositories[index].ReplaceByVersionAsync(
                        x => x.Id == id,
                        document,
                        document.Version,
                        token);
                })));

            var persisted = await _repository.SelectAsync(x => x.Id == id);
            Assert.NotNull(persisted);
            Assert.Equal(2, persisted.Value);
            Assert.Equal(3, persisted.Version);
            Assert.True(attempts.Sum() >= 3);
        }
        finally
        {
            foreach (var context in contexts)
            {
                await context.DisposeAsync();
            }

            await _repository.DeleteByFilterAsync(x => x.Id == id);
        }
    }

    [Fact]
    public async Task EnqueueOutsideTransaction_IsRejected()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _outbox.EnqueueAsync(Event("outside-transaction")));

        Assert.Contains("active MongoDB transaction", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoWorkers_ClaimTheBatchWithoutOverlap()
    {
        await _transactions.ExecuteAsync("WorkItems", async token =>
        {
            for (var index = 0; index < 80; index++)
            {
                await _outbox.EnqueueAsync(Event($"parallel-{index:000}"), token);
            }
        });

        var claims = await Task.WhenAll(
            _outbox.ClaimAsync("worker-a", 80, TimeSpan.FromMinutes(1), Now),
            _outbox.ClaimAsync("worker-b", 80, TimeSpan.FromMinutes(1), Now));
        var firstIds = claims[0].Select(x => x.Event.Id).ToHashSet(StringComparer.Ordinal);
        var secondIds = claims[1].Select(x => x.Event.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(firstIds.Intersect(secondIds, StringComparer.Ordinal));
        Assert.Equal(80, firstIds.Count + secondIds.Count);
        Assert.All(claims.SelectMany(x => x), lease => Assert.Equal(1, lease.Attempt));
    }

    [Fact]
    public async Task ExpiredLease_FencesTheOldWorkerAndAllowsTheNewOwnerToComplete()
    {
        await EnqueueAsync(Event("lease-fencing"));
        var oldLease = Assert.Single(await _outbox.ClaimAsync("old", 1, TimeSpan.FromSeconds(30), Now));
        var newLease = Assert.Single(await _outbox.ClaimAsync("new", 1, TimeSpan.FromSeconds(30), Now.AddSeconds(31)));

        Assert.NotEqual(oldLease.LeaseToken, newLease.LeaseToken);
        Assert.Equal(2, newLease.Attempt);
        Assert.False(await _outbox.CompleteAsync(oldLease.Event.Id, oldLease.LeaseToken, Now.AddSeconds(32)));
        Assert.True(await _outbox.CompleteAsync(newLease.Event.Id, newLease.LeaseToken, Now.AddSeconds(32)));
    }

    [Fact]
    public async Task Failure_UsesAvailabilityDeadLetterMetricsAndExplicitReplay()
    {
        await EnqueueAsync(Event("retry-dead-letter"));
        var first = Assert.Single(await _outbox.ClaimAsync("worker", 1, TimeSpan.FromSeconds(30), Now));
        var retryAt = Now.AddMinutes(1);
        var retry = await _outbox.FailAsync(first.Event.Id, first.LeaseToken, "transient", 2, Now, retryAt);

        Assert.True(retry.Updated);
        Assert.False(retry.DeadLettered);
        Assert.Empty(await _outbox.ClaimAsync("worker", 1, TimeSpan.FromSeconds(30), retryAt.AddTicks(-1)));

        var second = Assert.Single(await _outbox.ClaimAsync("worker", 1, TimeSpan.FromSeconds(30), retryAt));
        var dead = await _outbox.FailAsync(second.Event.Id, second.LeaseToken, "poison", 2, retryAt, retryAt.AddMinutes(1));
        Assert.True(dead.DeadLettered);
        var metrics = await _outbox.GetMetricsAsync(retryAt);
        Assert.Equal(1, metrics.DeadLetter);
        Assert.Equal(1, metrics.Retried);
        var listed = Assert.Single(await _outbox.ListDeadLettersAsync(1));
        Assert.Equal(second.Event.Id, listed.Id);
        Assert.Equal(second.Event.EventType, listed.EventType);
        Assert.Equal(2, listed.Attempts);

        Assert.True(await _outbox.ReplayDeadLetterAsync(second.Event.Id, retryAt.AddMinutes(2)));
        var replay = Assert.Single(await _outbox.ClaimAsync("replay", 1, TimeSpan.FromSeconds(30), retryAt.AddMinutes(2)));
        Assert.Equal(1, replay.Attempt);
    }

    [Fact]
    public async Task Inbox_DeduplicatesConcurrentConsumerCompletion()
    {
        var messageId = Guid.NewGuid().ToString("N");
        var results = await Task.WhenAll(
            _inbox.MarkProcessedAsync("audit-v1", messageId, Now),
            _inbox.MarkProcessedAsync("audit-v1", messageId, Now));

        Assert.Single(results, value => value);
        Assert.True(await _inbox.HasProcessedAsync("audit-v1", messageId));
        Assert.False(await _inbox.HasProcessedAsync("search-v1", messageId));
    }

    [Fact]
    public async Task ConsumerTargetAndInbox_CommitAndRollbackAcrossModuleDatabases()
    {
        var auditLogs = new MongoRepository<AuditLogDocument>(_mongo, _context);
        await _transactions.ExecuteAsync("WorkItems", async token =>
        {
            await auditLogs.CreateAsync(Audit("audit-committed", "dedupe-committed"), token);
            Assert.True(await _inbox.MarkProcessedAsync("audit-v1", "message-committed", Now, token));
        });

        Assert.True(await auditLogs.ExistsByFilterAsync(x => x.Id == "audit-committed"));
        Assert.True(await _inbox.HasProcessedAsync("audit-v1", "message-committed"));

        await Assert.ThrowsAsync<IntentionalFailure>(() =>
            _transactions.ExecuteAsync("WorkItems", async token =>
            {
                await auditLogs.CreateAsync(Audit("audit-rolled-back", "dedupe-rolled-back"), token);
                Assert.True(await _inbox.MarkProcessedAsync("audit-v1", "message-rolled-back", Now, token));
                throw new IntentionalFailure();
            }));

        Assert.False(await auditLogs.ExistsByFilterAsync(x => x.Id == "audit-rolled-back"));
        Assert.False(await _inbox.HasProcessedAsync("audit-v1", "message-rolled-back"));
    }

    private Task EnqueueAsync(DurableEventEnvelope message) =>
        _transactions.ExecuteAsync("WorkItems", token => _outbox.EnqueueAsync(message, token));

    private static DurableEventEnvelope Event(string key) =>
        DurableEventEnvelope.Create(
            "WorkItems",
            "work-item.test.v1",
            1,
            "tenant-data005",
            "correlation-data005",
            "{\"value\":\"test\"}",
            Now,
            key);

    private static AuditLogDocument Audit(string id, string deduplicationKey) => new()
    {
        Id = id,
        ActorUserId = "user-data005",
        Action = "WorkItemUpdated",
        EntityType = "WorkItem",
        EntityId = "work-item-data005",
        CorrelationId = "correlation-data005",
        DeduplicationKey = deduplicationKey,
        CreatedAt = Now
    };

    private sealed class IntentionalFailure : Exception
    {
    }
}
