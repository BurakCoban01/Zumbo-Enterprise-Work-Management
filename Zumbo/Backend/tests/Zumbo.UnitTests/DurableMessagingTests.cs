using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Zumbo.UnitTests;

public sealed class DurableMessagingTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ClaimAsync_AssignsEachMessageToOnlyOneWorker()
    {
        var outbox = new InMemoryDurableEventOutbox();
        for (var index = 0; index < 20; index++)
        {
            await outbox.EnqueueAsync(Message($"message-{index:D2}", Now.AddSeconds(index)));
        }

        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim(false);
        var firstWorker = ClaimFromWorkerAsync("worker-1");
        var secondWorker = ClaimFromWorkerAsync("worker-2");
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        start.Set();

        var claims = (await Task.WhenAll(firstWorker, secondWorker)).SelectMany(items => items).ToList();

        Assert.Equal(20, claims.Count);
        Assert.Equal(20, claims.Select(claim => claim.Event.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(claims, claim => Assert.Equal(1, claim.Attempt));
        Assert.Equal(10, claims.Count(claim => claim.WorkerId == "worker-1"));
        Assert.Equal(10, claims.Count(claim => claim.WorkerId == "worker-2"));
        Assert.Empty(await outbox.ClaimAsync("worker-3", 20, TimeSpan.FromMinutes(1), Now.AddMinutes(1)));

        Task<IReadOnlyList<DurableEventLease>> ClaimFromWorkerAsync(string workerId) => Task.Run(async () =>
        {
            ready.Signal();
            start.Wait();
            return await outbox.ClaimAsync(workerId, 10, TimeSpan.FromMinutes(1), Now.AddMinutes(1));
        });
    }

    [Fact]
    public async Task ClaimAsync_ReclaimsExpiredLeaseAndFencesStaleToken()
    {
        var outbox = new InMemoryDurableEventOutbox();
        var message = Message("message-1", Now);
        await outbox.EnqueueAsync(message);

        var firstLease = Assert.Single(await outbox.ClaimAsync(
            "worker-1",
            1,
            TimeSpan.FromMinutes(1),
            Now));
        Assert.Empty(await outbox.ClaimAsync(
            "worker-2",
            1,
            TimeSpan.FromMinutes(1),
            Now.AddSeconds(59)));

        var reclaimedLease = Assert.Single(await outbox.ClaimAsync(
            "worker-2",
            1,
            TimeSpan.FromMinutes(1),
            Now.AddMinutes(1)));

        Assert.Equal(2, reclaimedLease.Attempt);
        Assert.NotEqual(firstLease.LeaseToken, reclaimedLease.LeaseToken);
        Assert.False(await outbox.CompleteAsync(message.Id, firstLease.LeaseToken, Now.AddMinutes(1)));
        var staleFailure = await outbox.FailAsync(
            message.Id,
            firstLease.LeaseToken,
            "stale failure",
            3,
            Now.AddMinutes(1),
            Now.AddMinutes(2));
        Assert.False(staleFailure.Updated);
        Assert.True(await outbox.CompleteAsync(message.Id, reclaimedLease.LeaseToken, Now.AddMinutes(1)));
    }

    [Fact]
    public async Task Retry_DeadLetter_AndReplay_ResetTheMessageForProcessing()
    {
        var outbox = new InMemoryDurableEventOutbox();
        var message = Message("message-1", Now);
        await outbox.EnqueueAsync(message);

        var firstLease = Assert.Single(await outbox.ClaimAsync("worker", 1, TimeSpan.FromMinutes(1), Now));
        var retryAt = Now.AddMinutes(2);
        var retry = await outbox.FailAsync(
            message.Id,
            firstLease.LeaseToken,
            "temporary",
            2,
            Now,
            retryAt);

        Assert.True(retry.Updated);
        Assert.False(retry.DeadLettered);
        Assert.Equal(1, retry.Attempt);
        Assert.Equal(retryAt, retry.NextAttemptAtUtc);
        Assert.Empty(await outbox.ClaimAsync("worker", 1, TimeSpan.FromMinutes(1), retryAt.AddTicks(-1)));

        var secondLease = Assert.Single(await outbox.ClaimAsync("worker", 1, TimeSpan.FromMinutes(1), retryAt));
        Assert.Equal(2, secondLease.Attempt);
        var deadLetter = await outbox.FailAsync(
            message.Id,
            secondLease.LeaseToken,
            "permanent",
            2,
            retryAt,
            retryAt.AddMinutes(1));

        Assert.True(deadLetter.Updated);
        Assert.True(deadLetter.DeadLettered);
        Assert.Equal(2, deadLetter.Attempt);
        Assert.Null(deadLetter.NextAttemptAtUtc);
        Assert.True(await outbox.ReplayDeadLetterAsync(message.Id, retryAt.AddMinutes(2)));

        var replayedLease = Assert.Single(await outbox.ClaimAsync(
            "replay-worker",
            1,
            TimeSpan.FromMinutes(1),
            retryAt.AddMinutes(2)));
        Assert.Equal(1, replayedLease.Attempt);
        Assert.False(await outbox.ReplayDeadLetterAsync(message.Id, retryAt.AddMinutes(2)));
    }

    [Fact]
    public async Task EnqueueAsync_IgnoresDuplicateDeduplicationIdentity()
    {
        var outbox = new InMemoryDurableEventOutbox();
        await outbox.EnqueueAsync(Message("message-1", Now, "dedupe-1"));
        await outbox.EnqueueAsync(Message("message-2", Now.AddSeconds(1), "dedupe-1"));
        await outbox.EnqueueAsync(new DurableEventEnvelope(
            "message-3",
            "WorkItems",
            "work-item.deleted.v1",
            1,
            "tenant-1",
            "correlation-1",
            "dedupe-1",
            "{}",
            Now.AddSeconds(2)));

        var claimed = await outbox.ClaimAsync("worker", 10, TimeSpan.FromMinutes(1), Now.AddMinutes(1));

        Assert.Equal(2, claimed.Count);
        Assert.Contains(claimed, lease => lease.Event.Id == "message-1");
        Assert.Contains(claimed, lease => lease.Event.Id == "message-3");
    }

    [Fact]
    public async Task Inbox_AllowsOnlyOneConsumerMessagePairToBeMarked()
    {
        var inbox = new InMemoryDurableEventInbox();
        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim(false);
        var first = MarkAsync();
        var second = MarkAsync();
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        start.Set();

        var results = await Task.WhenAll(first, second);

        Assert.Single(results, result => result);
        Assert.True(await inbox.HasProcessedAsync("consumer-1", "message-1"));
        Assert.False(await inbox.HasProcessedAsync("consumer-2", "message-1"));

        Task<bool> MarkAsync() => Task.Run(async () =>
        {
            ready.Signal();
            start.Wait();
            return await inbox.MarkProcessedAsync("consumer-1", "message-1", Now);
        });
    }

    [Fact]
    public async Task GetMetricsAsync_ReportsStatesRetriesAndOldestPendingMessage()
    {
        var outbox = new InMemoryDurableEventOutbox();
        var completed = Message("completed", Now.AddMinutes(-4));
        var deadLetter = Message("dead-letter", Now.AddMinutes(-3));
        var retried = Message("retried", Now.AddMinutes(-2));
        var pending = Message("pending", Now.AddMinutes(-1));
        foreach (var message in new[] { completed, deadLetter, retried, pending })
        {
            await outbox.EnqueueAsync(message);
        }

        var initialClaims = await outbox.ClaimAsync("worker", 3, TimeSpan.FromMinutes(1), Now);
        Assert.True(await outbox.CompleteAsync(completed.Id, LeaseFor(initialClaims, completed.Id).LeaseToken, Now));
        await outbox.FailAsync(
            deadLetter.Id,
            LeaseFor(initialClaims, deadLetter.Id).LeaseToken,
            "dead",
            1,
            Now,
            Now.AddMinutes(1));
        await outbox.FailAsync(
            retried.Id,
            LeaseFor(initialClaims, retried.Id).LeaseToken,
            "retry",
            3,
            Now,
            Now.AddMinutes(1));
        _ = Assert.Single(await outbox.ClaimAsync(
            "retry-worker",
            1,
            TimeSpan.FromMinutes(1),
            Now.AddMinutes(1)));

        var capturedAt = Now.AddMinutes(1);
        var metrics = await outbox.GetMetricsAsync(capturedAt);

        Assert.Equal(1, metrics.Pending);
        Assert.Equal(1, metrics.Processing);
        Assert.Equal(1, metrics.DeadLetter);
        Assert.Equal(1, metrics.Completed);
        Assert.Equal(1, metrics.Retried);
        Assert.Equal(pending.OccurredAtUtc, metrics.OldestPendingAtUtc);
        Assert.Equal(capturedAt, metrics.CapturedAtUtc);
    }

    [Theory]
    [InlineData(0, 1, 750)]
    [InlineData(1, 1, 1250)]
    [InlineData(0, 3, 3000)]
    [InlineData(1, 3, 5000)]
    [InlineData(0, 4, 6000)]
    [InlineData(1, 4, 8000)]
    public void DurableMessageRetryPolicy_ClampsJitterWithinConfiguredBounds(
        double jitterValue,
        int attempt,
        double expectedMilliseconds)
    {
        var policy = new DurableMessageRetryPolicy(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(8),
            0.25,
            new FixedJitter(jitterValue));

        var delay = policy.DelayForAttempt(attempt);

        Assert.Equal(expectedMilliseconds, delay.TotalMilliseconds);
    }

    [Fact]
    public async Task InMemoryTransactionRunner_ExecutesWithinTheProvidedCancellationBoundary()
    {
        var runner = new InMemoryDurableTransactionRunner();
        using var cancellation = new CancellationTokenSource();

        var result = await runner.ExecuteAsync("WorkItems", token =>
        {
            Assert.Equal(cancellation.Token, token);
            return Task.FromResult(42);
        }, cancellation.Token);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Processor_RetriesTransientConsumerAndEventuallyCompletesExactlyOnce()
    {
        var outbox = new InMemoryDurableEventOutbox();
        var inbox = new InMemoryDurableEventInbox();
        var time = new ManualTimeProvider(Now);
        var handler = new FailingHandler(failuresBeforeSuccess: 1);
        await outbox.EnqueueAsync(Message("eventual", Now));
        var processor = Processor(outbox, inbox, time, handler, maximumAttempts: 3);

        Assert.Equal(1, await processor.ProcessBatchAsync("worker"));
        Assert.Equal(1, handler.Attempts);
        Assert.Equal(1, (await outbox.GetMetricsAsync(time.GetUtcNow())).Pending);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await processor.ProcessBatchAsync("worker"));
        Assert.Equal(2, handler.Attempts);
        Assert.True(await inbox.HasProcessedAsync(handler.ConsumerName, "eventual"));
        var metrics = await outbox.GetMetricsAsync(time.GetUtcNow());
        Assert.Equal(1, metrics.Completed);
        Assert.Equal(1, metrics.Retried);
    }

    [Fact]
    public async Task Processor_DeadLettersPoisonConsumerAndReplayCanRecover()
    {
        var outbox = new InMemoryDurableEventOutbox();
        var inbox = new InMemoryDurableEventInbox();
        var time = new ManualTimeProvider(Now);
        var handler = new FailingHandler(failuresBeforeSuccess: int.MaxValue);
        await outbox.EnqueueAsync(Message("poison", Now));
        var processor = Processor(outbox, inbox, time, handler, maximumAttempts: 2);

        await processor.ProcessBatchAsync("worker");
        time.Advance(TimeSpan.FromSeconds(1));
        await processor.ProcessBatchAsync("worker");

        Assert.Equal(1, (await outbox.GetMetricsAsync(time.GetUtcNow())).DeadLetter);
        Assert.True(await outbox.ReplayDeadLetterAsync("poison", time.GetUtcNow()));
        handler.FailuresBeforeSuccess = handler.Attempts;
        await processor.ProcessBatchAsync("worker");
        Assert.Equal(1, (await outbox.GetMetricsAsync(time.GetUtcNow())).Completed);
    }

    [Fact]
    public async Task Processor_UsesInboxToSkipAnAlreadyAppliedConsumerEffect()
    {
        var outbox = new InMemoryDurableEventOutbox();
        var inbox = new InMemoryDurableEventInbox();
        var time = new ManualTimeProvider(Now);
        var handler = new FailingHandler(0);
        await outbox.EnqueueAsync(Message("already-applied", Now));
        Assert.True(await inbox.MarkProcessedAsync(handler.ConsumerName, "already-applied", Now));

        await Processor(outbox, inbox, time, handler, 3).ProcessBatchAsync("worker");

        Assert.Equal(0, handler.Attempts);
        Assert.Equal(1, (await outbox.GetMetricsAsync(time.GetUtcNow())).Completed);
    }

    private static DurableEventEnvelope Message(
        string id,
        DateTimeOffset occurredAtUtc,
        string? deduplicationKey = null) =>
        new(
            id,
            "WorkItems",
            "work-item.changed.v1",
            1,
            "tenant-1",
            "correlation-1",
            deduplicationKey,
            "{}",
            occurredAtUtc);

    private static DurableEventLease LeaseFor(IEnumerable<DurableEventLease> leases, string messageId) =>
        Assert.Single(leases, lease => lease.Event.Id == messageId);

    private static DurableEventProcessor Processor(
        IDurableEventOutbox outbox,
        IDurableEventInbox inbox,
        TimeProvider time,
        IDurableEventHandler handler,
        int maximumAttempts) =>
        new(
            outbox,
            inbox,
            new InMemoryDurableTransactionRunner(),
            [handler],
            new FixedJitter(0.5),
            time,
            Options.Create(new DurableEventProcessorOptions
            {
                BatchSize = 10,
                MaximumAttempts = maximumAttempts,
                LeaseDuration = TimeSpan.FromMinutes(1),
                BaseRetryDelay = TimeSpan.FromSeconds(1),
                MaximumRetryDelay = TimeSpan.FromMinutes(1),
                RetryJitterRatio = 0
            }),
            NullLogger<DurableEventProcessor>.Instance);

    private sealed class FixedJitter(double value) : IDurableMessageJitter
    {
        public double NextUnit() => value;
    }

    private sealed class FailingHandler(int failuresBeforeSuccess) : IDurableEventHandler
    {
        public int Attempts { get; private set; }
        public int FailuresBeforeSuccess { get; set; } = failuresBeforeSuccess;
        public string ConsumerName => "test-consumer-v1";
        public string EventType => "work-item.changed.v1";

        public Task HandleAsync(DurableEventEnvelope message, CancellationToken cancellationToken)
        {
            Attempts++;
            if (Attempts <= FailuresBeforeSuccess)
            {
                throw new InvalidOperationException("Injected consumer dependency failure.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
        public override long GetTimestamp() => _now.UtcTicks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
