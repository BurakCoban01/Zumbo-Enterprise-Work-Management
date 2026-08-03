namespace Zumbo.BuildingBlocks.Application.Concurrency;

public sealed class DistributedLockOptions
{
    public string Provider { get; init; } = "InMemory";
    public int LeaseSeconds { get; init; } = 30;
    public int WaitSeconds { get; init; } = 5;
}
