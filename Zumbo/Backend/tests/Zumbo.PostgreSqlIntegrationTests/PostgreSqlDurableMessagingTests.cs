using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.RepositoryContracts;
using Zumbo.Modules.Audit;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PostgreSqlDurableMessagingTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BusinessWriteAndOutbox_CommitAndRollbackAtomically()
    {
        await fixture.Api.ResetDurableMessagingAsync(CancellationToken.None);
        var repository = Repository();
        var committedId = $"outbox-commit-{Guid.NewGuid():N}";
        var rolledBackId = $"outbox-rollback-{Guid.NewGuid():N}";

        await fixture.Api.ExecuteInTransactionAsync(async token =>
        {
            await repository.CreateAsync(new RepositoryContractDocument { Id = committedId, Name = "committed" }, token);
            await fixture.Api.Outbox.EnqueueAsync(Event("commit-event"), token);
        }, CancellationToken.None);

        Assert.True(await repository.ExistsByFilterAsync(document => document.Id == committedId));
        Assert.Equal(1, (await fixture.Api.Outbox.GetMetricsAsync(Now)).Pending);

        await Assert.ThrowsAsync<IntentionalFailure>(() =>
            fixture.Api.ExecuteInTransactionAsync(async token =>
            {
                await repository.CreateAsync(new RepositoryContractDocument { Id = rolledBackId, Name = "rolled-back" }, token);
                await fixture.Api.Outbox.EnqueueAsync(Event("rollback-event"), token);
                throw new IntentionalFailure();
            }, CancellationToken.None));

        Assert.False(await repository.ExistsByFilterAsync(document => document.Id == rolledBackId));
        Assert.Equal(1, (await fixture.Api.Outbox.GetMetricsAsync(Now)).Pending);
        await repository.DeleteByFilterAsync(document => document.Id == committedId);
    }

    [Fact]
    public async Task EnqueueOutsideTransaction_IsRejected()
    {
        await fixture.Api.ResetDurableMessagingAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Api.Outbox.EnqueueAsync(Event("outside-transaction")));

        Assert.Contains("active PostgreSQL transaction", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoWorkers_ClaimTheBatchWithoutOverlap()
    {
        await fixture.Api.ResetDurableMessagingAsync(CancellationToken.None);
        await fixture.Api.ExecuteInTransactionAsync(async token =>
        {
            for (var index = 0; index < 80; index++)
            {
                await fixture.Api.Outbox.EnqueueAsync(Event($"parallel-{index:000}"), token);
            }
        }, CancellationToken.None);

        var claims = await Task.WhenAll(
            fixture.Api.Outbox.ClaimAsync("worker-a", 80, TimeSpan.FromMinutes(1), Now),
            fixture.Api.Outbox.ClaimAsync("worker-b", 80, TimeSpan.FromMinutes(1), Now));
        var firstIds = claims[0].Select(item => item.Event.Id).ToHashSet(StringComparer.Ordinal);
        var secondIds = claims[1].Select(item => item.Event.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(firstIds.Intersect(secondIds, StringComparer.Ordinal));
        Assert.Equal(80, firstIds.Count + secondIds.Count);
        Assert.All(claims.SelectMany(items => items), item => Assert.Equal(1, item.Attempt));
    }

    [Fact]
    public async Task ExpiredLease_FencesTheOldWorkerAndAllowsTheNewOwnerToComplete()
    {
        await fixture.Api.ResetDurableMessagingAsync(CancellationToken.None);
        await EnqueueAsync(Event("lease-fencing"));
        var oldLease = Assert.Single(await fixture.Api.Outbox.ClaimAsync(
            "worker-old", 1, TimeSpan.FromSeconds(30), Now));
        var newLease = Assert.Single(await fixture.Api.Outbox.ClaimAsync(
            "worker-new", 1, TimeSpan.FromSeconds(30), Now.AddSeconds(31)));

        Assert.NotEqual(oldLease.LeaseToken, newLease.LeaseToken);
        Assert.Equal(2, newLease.Attempt);
        Assert.False(await fixture.Api.Outbox.CompleteAsync(oldLease.Event.Id, oldLease.LeaseToken, Now.AddSeconds(32)));
        Assert.True(await fixture.Api.Outbox.CompleteAsync(newLease.Event.Id, newLease.LeaseToken, Now.AddSeconds(32)));
    }

    [Fact]
    public async Task Failure_UsesAvailabilityDeadLetterMetricsAndExplicitReplay()
    {
        await fixture.Api.ResetDurableMessagingAsync(CancellationToken.None);
        await EnqueueAsync(Event("retry-dead-letter"));
        var first = Assert.Single(await fixture.Api.Outbox.ClaimAsync(
            "worker", 1, TimeSpan.FromSeconds(30), Now));
        var nextAttempt = Now.AddMinutes(1);

        var retry = await fixture.Api.Outbox.FailAsync(
            first.Event.Id,
            first.LeaseToken,
            "transient",
            maximumAttempts: 2,
            nowUtc: Now,
            nextAttemptAtUtc: nextAttempt);
        Assert.True(retry.Updated);
        Assert.False(retry.DeadLettered);
        Assert.Empty(await fixture.Api.Outbox.ClaimAsync(
            "worker", 1, TimeSpan.FromSeconds(30), nextAttempt.AddTicks(-1)));

        var second = Assert.Single(await fixture.Api.Outbox.ClaimAsync(
            "worker", 1, TimeSpan.FromSeconds(30), nextAttempt));
        var dead = await fixture.Api.Outbox.FailAsync(
            second.Event.Id,
            second.LeaseToken,
            "poison",
            maximumAttempts: 2,
            nowUtc: nextAttempt,
            nextAttemptAtUtc: nextAttempt.AddMinutes(2));
        Assert.True(dead.DeadLettered);
        var metrics = await fixture.Api.Outbox.GetMetricsAsync(nextAttempt);
        Assert.Equal(1, metrics.DeadLetter);
        Assert.Equal(1, metrics.Retried);
        var listed = Assert.Single(await fixture.Api.Outbox.ListDeadLettersAsync(1));
        Assert.Equal(second.Event.Id, listed.Id);
        Assert.Equal(second.Event.EventType, listed.EventType);
        Assert.Equal(2, listed.Attempts);

        Assert.True(await fixture.Api.Outbox.ReplayDeadLetterAsync(second.Event.Id, nextAttempt.AddMinutes(3)));
        var replay = Assert.Single(await fixture.Api.Outbox.ClaimAsync(
            "worker-replay", 1, TimeSpan.FromSeconds(30), nextAttempt.AddMinutes(3)));
        Assert.Equal(1, replay.Attempt);
    }

    [Fact]
    public async Task Inbox_DeduplicatesConcurrentConsumerCompletion()
    {
        await fixture.Api.ResetDurableMessagingAsync(CancellationToken.None);
        var messageId = Guid.NewGuid().ToString("N");

        var results = await Task.WhenAll(
            fixture.Api.Inbox.MarkProcessedAsync("audit-v1", messageId, Now),
            fixture.Api.Inbox.MarkProcessedAsync("audit-v1", messageId, Now));

        Assert.Single(results, value => value);
        Assert.True(await fixture.Api.Inbox.HasProcessedAsync("audit-v1", messageId));
        Assert.False(await fixture.Api.Inbox.HasProcessedAsync("search-v1", messageId));
    }

    [Fact]
    public async Task ConsumerTargetAndInbox_CommitAndRollbackAcrossModuleSchemas()
    {
        await fixture.Api.ResetDurableMessagingAsync(CancellationToken.None);
        var auditLogs = fixture.Api.CreateRepository<AuditLogDocument>("audit", "audit_logs");
        await auditLogs.DeleteByFilterAsync(x =>
            x.Id == "audit-committed" || x.Id == "audit-rolled-back");
        await fixture.Api.ExecuteInTransactionAsync(async token =>
        {
            await auditLogs.CreateAsync(Audit("audit-committed", "dedupe-committed"), token);
            Assert.True(await fixture.Api.Inbox.MarkProcessedAsync(
                "audit-v1", "message-committed", Now, token));
        }, CancellationToken.None);

        Assert.True(await auditLogs.ExistsByFilterAsync(x => x.Id == "audit-committed"));
        Assert.True(await fixture.Api.Inbox.HasProcessedAsync("audit-v1", "message-committed"));

        await Assert.ThrowsAsync<IntentionalFailure>(() =>
            fixture.Api.ExecuteInTransactionAsync(async token =>
            {
                await auditLogs.CreateAsync(Audit("audit-rolled-back", "dedupe-rolled-back"), token);
                Assert.True(await fixture.Api.Inbox.MarkProcessedAsync(
                    "audit-v1", "message-rolled-back", Now, token));
                throw new IntentionalFailure();
            }, CancellationToken.None));

        Assert.False(await auditLogs.ExistsByFilterAsync(x => x.Id == "audit-rolled-back"));
        Assert.False(await fixture.Api.Inbox.HasProcessedAsync("audit-v1", "message-rolled-back"));
        await auditLogs.DeleteByFilterAsync(x => x.Id == "audit-committed");
    }

    private async Task EnqueueAsync(DurableEventEnvelope message) =>
        await fixture.Api.ExecuteInTransactionAsync(
            token => fixture.Api.Outbox.EnqueueAsync(message, token),
            CancellationToken.None);

    private Zumbo.BuildingBlocks.Application.Persistence.IDocumentRepository<RepositoryContractDocument> Repository() =>
        fixture.Api.CreateRepository<RepositoryContractDocument>(
            PostgreSqlFixture.TestSchema,
            PostgreSqlFixture.RepositoryTable);

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
