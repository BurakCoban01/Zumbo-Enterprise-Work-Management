namespace Zumbo.BuildingBlocks.Application.Runtime;

public sealed record ExternalDependencySnapshot(
    string Dependency,
    long Executions,
    long Attempts,
    long Retries,
    long Succeeded,
    long Failed,
    long TimedOut,
    long Rejected,
    long Cancelled,
    int InFlight,
    int Queued,
    bool CircuitOpen,
    double AverageLatencyMilliseconds);
