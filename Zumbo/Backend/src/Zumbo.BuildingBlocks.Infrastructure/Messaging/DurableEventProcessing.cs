using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;

namespace Zumbo.BuildingBlocks.Infrastructure.Messaging;

public sealed class DurableEventProcessorOptions
{
    public int BatchSize { get; init; } = 50;
    public int MaximumAttempts { get; init; } = 8;
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan BaseRetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromMinutes(5);
    public double RetryJitterRatio { get; init; } = 0.2;
    public TimeSpan IdleDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    public void Validate()
    {
        if (BatchSize is < 1 or > 500) throw new InvalidOperationException("Durable event batch size must be between 1 and 500.");
        if (MaximumAttempts is < 1 or > 100) throw new InvalidOperationException("Durable event maximum attempts must be between 1 and 100.");
        if (LeaseDuration <= TimeSpan.Zero || LeaseDuration > TimeSpan.FromMinutes(15)) throw new InvalidOperationException("Durable event lease duration is invalid.");
        if (BaseRetryDelay <= TimeSpan.Zero || MaximumRetryDelay < BaseRetryDelay) throw new InvalidOperationException("Durable event retry delay is invalid.");
        if (RetryJitterRatio is < 0 or > 1) throw new InvalidOperationException("Durable event retry jitter ratio is invalid.");
        if (IdleDelay < TimeSpan.Zero || IdleDelay > TimeSpan.FromMinutes(1)) throw new InvalidOperationException("Durable event idle delay is invalid.");
    }
}

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

public sealed class DurableEventWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<DurableEventProcessorOptions> configuredOptions,
    ILogger<DurableEventWorker> logger) : BackgroundService
{
    private readonly DurableEventProcessorOptions _options = Validate(configuredOptions.Value);
    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Durable event worker {WorkerId} started", _workerId);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<DurableEventProcessor>();
                var claimed = await processor.ProcessBatchAsync(_workerId, stoppingToken);
                if (claimed == 0 && _options.IdleDelay > TimeSpan.Zero)
                {
                    await Task.Delay(_options.IdleDelay, timeProvider, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Durable event worker cycle failed");
                await Task.Delay(_options.IdleDelay, timeProvider, stoppingToken);
            }
        }
    }

    private static DurableEventProcessorOptions Validate(DurableEventProcessorOptions options)
    {
        options.Validate();
        return options;
    }
}
