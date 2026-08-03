using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;

namespace Zumbo.BuildingBlocks.Infrastructure.Messaging;

public sealed class DurableEventProcessor(
    IDurableEventOutbox outbox,
    IDurableEventInbox inbox,
    IDurableTransactionRunner transactions,
    IEnumerable<IDurableEventHandler> handlers,
    IDurableMessageJitter jitter,
    TimeProvider timeProvider,
    IOptions<DurableEventProcessorOptions> configuredOptions,
    ILogger<DurableEventProcessor> logger)
{
    private static readonly Meter Meter = new("Zumbo.DurableMessaging", "1.0.0");
    private static readonly ActivitySource ActivitySource = new("Zumbo.DurableMessaging", "1.0.0");
    private static readonly Counter<long> Claimed = Meter.CreateCounter<long>("zumbo.outbox.claimed");
    private static readonly Counter<long> Completed = Meter.CreateCounter<long>("zumbo.outbox.completed");
    private static readonly Counter<long> Retried = Meter.CreateCounter<long>("zumbo.outbox.retried");
    private static readonly Counter<long> Failed = Meter.CreateCounter<long>("zumbo.outbox.failures");
    private static readonly Counter<long> DeadLettered = Meter.CreateCounter<long>("zumbo.outbox.dead_lettered");
    private static readonly Histogram<double> PendingAge = Meter.CreateHistogram<double>("zumbo.outbox.pending_age", "s");
    private static readonly Histogram<double> Throughput = Meter.CreateHistogram<double>("zumbo.outbox.batch_throughput", "{event}/s");
    private readonly DurableEventProcessorOptions _options = Validate(configuredOptions.Value);
    private readonly IReadOnlyDictionary<string, IDurableEventHandler> _handlers = handlers
        .GroupBy(handler => handler.EventType, StringComparer.Ordinal)
        .ToDictionary(
            group => group.Key,
            group => group.Count() == 1
                ? group.Single()
                : throw new InvalidOperationException($"Durable event type '{group.Key}' has multiple handlers."),
            StringComparer.Ordinal);

    public async Task<int> ProcessBatchAsync(string workerId, CancellationToken cancellationToken = default)
    {
        using var batchActivity = ActivitySource.StartActivity("outbox.process_batch", ActivityKind.Internal);
        var started = timeProvider.GetTimestamp();
        var now = timeProvider.GetUtcNow();
        var leases = await outbox.ClaimAsync(
            workerId,
            _options.BatchSize,
            _options.LeaseDuration,
            now,
            cancellationToken);
        Claimed.Add(leases.Count);
        batchActivity?.SetTag("messaging.batch.message_count", leases.Count);

        foreach (var lease in leases)
        {
            using var consumeActivity = ActivitySource.StartActivity("outbox.consume", ActivityKind.Consumer);
            consumeActivity?.SetTag("messaging.system", "zumbo-outbox");
            consumeActivity?.SetTag("messaging.operation", "process");
            consumeActivity?.SetTag("messaging.message.type", lease.Event.EventType);
            consumeActivity?.SetTag("zumbo.correlation_id", lease.Event.CorrelationId);
            try
            {
                if (!_handlers.TryGetValue(lease.Event.EventType, out var handler))
                {
                    throw new InvalidOperationException(
                        $"No durable event handler is registered for '{lease.Event.EventType}'.");
                }

                await transactions.ExecuteAsync(
                    lease.Event.OwnerModule,
                    async token =>
                    {
                        if (await inbox.HasProcessedAsync(handler.ConsumerName, lease.Event.Id, token))
                        {
                            return;
                        }

                        await handler.HandleAsync(lease.Event, token);
                        _ = await inbox.MarkProcessedAsync(
                            handler.ConsumerName,
                            lease.Event.Id,
                            timeProvider.GetUtcNow(),
                            token);
                    },
                    cancellationToken);

                if (await outbox.CompleteAsync(
                    lease.Event.Id,
                    lease.LeaseToken,
                    timeProvider.GetUtcNow(),
                    cancellationToken))
                {
                    Completed.Add(1, new KeyValuePair<string, object?>("event.type", lease.Event.EventType));
                    consumeActivity?.SetStatus(ActivityStatusCode.Ok);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                consumeActivity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
                Failed.Add(1, new KeyValuePair<string, object?>("event.type", lease.Event.EventType));
                var retryPolicy = new DurableMessageRetryPolicy(
                    _options.BaseRetryDelay,
                    _options.MaximumRetryDelay,
                    _options.RetryJitterRatio,
                    jitter);
                var failedAt = timeProvider.GetUtcNow();
                var failure = await outbox.FailAsync(
                    lease.Event.Id,
                    lease.LeaseToken,
                    exception.GetType().Name + ": " + exception.Message,
                    _options.MaximumAttempts,
                    failedAt,
                    failedAt.Add(retryPolicy.DelayForAttempt(lease.Attempt)),
                    cancellationToken);
                if (failure.DeadLettered)
                {
                    DeadLettered.Add(1, new KeyValuePair<string, object?>("event.type", lease.Event.EventType));
                }
                else if (failure.Updated)
                {
                    Retried.Add(1, new KeyValuePair<string, object?>("event.type", lease.Event.EventType));
                }

                logger.LogWarning(
                    exception,
                    "Durable event {EventId} ({EventType}) failed on attempt {Attempt}; dead-letter={DeadLettered}",
                    lease.Event.Id,
                    lease.Event.EventType,
                    lease.Attempt,
                    failure.DeadLettered);
            }
        }

        var metrics = await outbox.GetMetricsAsync(timeProvider.GetUtcNow(), cancellationToken);
        if (metrics.OldestPendingAtUtc is { } oldest)
        {
            PendingAge.Record(Math.Max(0, (metrics.CapturedAtUtc - oldest).TotalSeconds));
        }

        var elapsedSeconds = timeProvider.GetElapsedTime(started).TotalSeconds;
        if (leases.Count > 0 && elapsedSeconds > 0)
        {
            Throughput.Record(leases.Count / elapsedSeconds);
        }

        batchActivity?.SetStatus(ActivityStatusCode.Ok);
        return leases.Count;
    }

    private static DurableEventProcessorOptions Validate(DurableEventProcessorOptions options)
    {
        options.Validate();
        return options;
    }
}
