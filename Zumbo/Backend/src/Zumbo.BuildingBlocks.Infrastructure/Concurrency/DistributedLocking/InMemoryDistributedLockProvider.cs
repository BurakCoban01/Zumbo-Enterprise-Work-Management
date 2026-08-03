using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.BuildingBlocks.Infrastructure.Concurrency;

public sealed class InMemoryDistributedLockProvider : IDistributedLockProvider
{
    private readonly ConcurrentDictionary<string, LockEntry> _locks = new(StringComparer.Ordinal);

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string resource,
        TimeSpan leaseTime,
        TimeSpan waitTime,
        CancellationToken ct = default)
    {
        LockEntry entry;
        while (true)
        {
            entry = _locks.GetOrAdd(resource, _ => new LockEntry());
            lock (entry.SyncRoot)
            {
                if (entry.Removed)
                {
                    continue;
                }

                entry.References++;
                break;
            }
        }

        var acquired = false;
        try
        {
            acquired = await entry.Semaphore.WaitAsync(waitTime, ct);
            return acquired ? new SemaphoreReleaser(this, resource, entry) : null;
        }
        finally
        {
            if (!acquired)
            {
                ReleaseReference(resource, entry);
            }
        }
    }

    private void ReleaseReference(string resource, LockEntry entry)
    {
        lock (entry.SyncRoot)
        {
            entry.References--;
            if (entry.References != 0)
            {
                return;
            }

            entry.Removed = true;
            ((ICollection<KeyValuePair<string, LockEntry>>)_locks)
                .Remove(new KeyValuePair<string, LockEntry>(resource, entry));
            entry.Semaphore.Dispose();
        }
    }

    private sealed class LockEntry
    {
        public object SyncRoot { get; } = new();
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int References { get; set; }
        public bool Removed { get; set; }
    }

    private sealed class SemaphoreReleaser(
        InMemoryDistributedLockProvider owner,
        string resource,
        LockEntry entry) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                entry.Semaphore.Release();
                owner.ReleaseReference(resource, entry);
            }

            return ValueTask.CompletedTask;
        }
    }
}
