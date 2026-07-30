using System.Diagnostics.Metrics;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.UnitTests;

public sealed class CompensationExecutionTests
{
    [Fact]
    public async Task RunAsync_UsesBoundedInternalTokenAndRecordsSuccess()
    {
        CancellationToken observed = default;
        var measurements = new List<(long Value, string? Operation, string? Outcome)>();
        using var listener = CreateListener(measurements);

        var result = await CompensationExecution.RunAsync(
            "test.cleanup",
            token =>
            {
                observed = token;
                return Task.CompletedTask;
            },
            TimeSpan.FromSeconds(1));

        Assert.True(observed.CanBeCanceled);
        Assert.True(result.Succeeded);
        Assert.Equal(CompensationOutcome.Succeeded, result.Outcome);
        Assert.Contains(measurements, item =>
            item.Value == 1
            && item.Operation == "test.cleanup"
            && item.Outcome == "succeeded");
    }

    [Fact]
    public async Task RunAsync_CapturesFailureWithoutThrowing()
    {
        var expected = new InvalidOperationException("Synthetic cleanup failure.");

        var result = await CompensationExecution.RunAsync(
            "test.failure",
            _ => Task.FromException(expected),
            TimeSpan.FromSeconds(1));

        Assert.Equal(CompensationOutcome.Failed, result.Outcome);
        Assert.Same(expected, result.Exception);
    }

    [Fact]
    public async Task RunAsync_BoundsAnOperationThatDoesNotComplete()
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var result = await CompensationExecution.RunAsync(
            "test.timeout",
            _ => completion.Task,
            TimeSpan.FromMilliseconds(20));

        Assert.Equal(CompensationOutcome.TimedOut, result.Outcome);
        Assert.InRange(result.Duration, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData("tenant/secret")]
    [InlineData("operation with spaces")]
    [InlineData("")]
    public async Task RunAsync_RejectsDynamicMetricLabels(string operation)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CompensationExecution.RunAsync(operation, _ => Task.CompletedTask));
    }

    private static MeterListener CreateListener(
        ICollection<(long Value, string? Operation, string? Outcome)> measurements)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, candidate) =>
        {
            if (instrument.Meter.Name == "Zumbo.Compensation"
                && instrument.Name == "zumbo.compensation.outcomes")
            {
                candidate.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            string? operation = null;
            string? outcome = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "operation")
                {
                    operation = tag.Value?.ToString();
                }
                else if (tag.Key == "outcome")
                {
                    outcome = tag.Value?.ToString();
                }
            }

            measurements.Add((value, operation, outcome));
        });
        listener.Start();
        return listener;
    }
}
