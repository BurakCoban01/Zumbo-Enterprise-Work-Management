namespace Zumbo.BuildingBlocks.Application.Concurrency;

public interface IDistributedLockProvider
{
    Task<IAsyncDisposable?> TryAcquireAsync(
        string resource,
        TimeSpan leaseTime,
        TimeSpan waitTime,
        CancellationToken ct = default);
}
