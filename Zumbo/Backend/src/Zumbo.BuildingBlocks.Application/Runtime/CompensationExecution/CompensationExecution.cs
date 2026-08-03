using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Zumbo.BuildingBlocks.Application.Runtime;

public static class CompensationExecution
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private static readonly Meter Meter = new("Zumbo.Compensation", "1.0.0");
    private static readonly Counter<long> Outcomes =
        Meter.CreateCounter<long>("zumbo.compensation.outcomes", unit: "{operation}");
    private static readonly Histogram<double> Duration =
        Meter.CreateHistogram<double>("zumbo.compensation.duration", unit: "ms");

    public static async Task<CompensationResult> RunAsync(
        string operation,
        Func<CancellationToken, Task> action,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        EnsureOperationName(operation);
        var budget = timeout ?? DefaultTimeout;
        if (budget < TimeSpan.FromMilliseconds(1) || budget > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "Compensation timeout must be between 1 millisecond and 2 minutes.");
        }

        var started = Stopwatch.GetTimestamp();
        using var cancellation = new CancellationTokenSource(budget);
        Task? execution = null;
        CompensationOutcome outcome;
        Exception? failure = null;
        try
        {
            execution = action(cancellation.Token);
            await execution.WaitAsync(cancellation.Token);
            outcome = CompensationOutcome.Succeeded;
        }
        catch (OperationCanceledException exception) when (cancellation.IsCancellationRequested)
        {
            ObserveLateFailure(execution);
            outcome = CompensationOutcome.TimedOut;
            failure = exception;
        }
        catch (Exception exception)
        {
            outcome = CompensationOutcome.Failed;
            failure = exception;
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var tags = new TagList
        {
            { "operation", operation },
            { "outcome", outcome.ToString().ToLowerInvariant() }
        };
        Outcomes.Add(1, tags);
        Duration.Record(elapsed.TotalMilliseconds, tags);
        return new CompensationResult(operation, outcome, elapsed, failure);
    }

    private static void EnsureOperationName(string operation)
    {
        if (string.IsNullOrWhiteSpace(operation)
            || operation.Length > 80
            || operation.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')))
        {
            throw new ArgumentException(
                "Compensation operation must be a fixed ASCII identifier.",
                nameof(operation));
        }
    }

    private static void ObserveLateFailure(Task? execution)
    {
        if (execution is null || execution.IsCompleted)
        {
            return;
        }

        _ = execution.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
