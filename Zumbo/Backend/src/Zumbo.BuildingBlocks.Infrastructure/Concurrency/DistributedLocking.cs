using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.BuildingBlocks.Infrastructure.Concurrency;

public static class DistributedLockServiceCollectionExtensions
{
    public static IServiceCollection AddZumboDistributedLocking(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DistributedLockOptions>(configuration.GetSection("DistributedLock"));
        services.Configure<RedisLockOptions>(configuration.GetSection("DistributedLock:Redis"));
        var provider = configuration.GetValue<string>("DistributedLock:Provider") ?? "InMemory";
        if (!provider.Equals("Redis", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IDistributedLockProvider, InMemoryDistributedLockProvider>();
            return services;
        }

        var redisOptions = configuration.GetSection("DistributedLock:Redis").Get<RedisLockOptions>()
            ?? new RedisLockOptions();
        var connectionOptions = ConfigurationOptions.Parse(redisOptions.ConnectionString);
        connectionOptions.AbortOnConnectFail = false;
        connectionOptions.ConnectTimeout = redisOptions.ConnectTimeoutMilliseconds;
        connectionOptions.AsyncTimeout = redisOptions.AsyncTimeoutMilliseconds;
        connectionOptions.SyncTimeout = redisOptions.SyncTimeoutMilliseconds;
        connectionOptions.ConnectRetry = redisOptions.ConnectRetry;
        connectionOptions.KeepAlive = redisOptions.KeepAliveSeconds;
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connectionOptions));
        services.AddSingleton<IDistributedLockProvider, RedisDistributedLockProvider>();
        return services;
    }
}

public sealed class RedisLockOptions
{
    public string ConnectionString { get; init; } = string.Empty;
    public string KeyPrefix { get; init; } = "zumbo:lock:";
    public int RetryMilliseconds { get; init; } = 100;
    public int ConnectTimeoutMilliseconds { get; init; } = 5_000;
    public int AsyncTimeoutMilliseconds { get; init; } = 1_000;
    public int SyncTimeoutMilliseconds { get; init; } = 1_000;
    public int ConnectRetry { get; init; } = 2;
    public int KeepAliveSeconds { get; init; } = 60;
}

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

public sealed class RedisDistributedLockProvider : IDistributedLockProvider
{
    private readonly IConnectionMultiplexer connection;
    private readonly IOptions<RedisLockOptions> redisOptions;
    private readonly IExternalDependencyPolicy? resiliencePolicy;
    private const string RenewScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then "
        + "return redis.call('pexpire', KEYS[1], ARGV[2]) else return 0 end";
    private const string ReleaseScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then "
        + "return redis.call('del', KEYS[1]) else return 0 end";

    public RedisDistributedLockProvider(
        IConnectionMultiplexer connection,
        IOptions<RedisLockOptions> redisOptions)
        : this(connection, redisOptions, null)
    {
    }

    public RedisDistributedLockProvider(
        IConnectionMultiplexer connection,
        IOptions<RedisLockOptions> redisOptions,
        IExternalDependencyPolicyProvider? policyProvider)
    {
        this.connection = connection;
        this.redisOptions = redisOptions;
        resiliencePolicy = policyProvider?.Get(ExternalDependencyNames.Redis);
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(
        string resource,
        TimeSpan leaseTime,
        TimeSpan waitTime,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(resource))
        {
            throw new ArgumentException("Lock resource is required.", nameof(resource));
        }

        var options = redisOptions.Value;
        var database = connection.GetDatabase();
        var key = (RedisKey)(options.KeyPrefix + resource);
        var ownerToken = Guid.NewGuid().ToString("N");
        var deadline = DateTimeOffset.UtcNow.Add(waitTime);
        var retry = TimeSpan.FromMilliseconds(Math.Clamp(options.RetryMilliseconds, 20, 2000));

        do
        {
            ct.ThrowIfCancellationRequested();
            var acquired = resiliencePolicy is null
                ? await database.StringSetAsync(key, ownerToken, leaseTime, When.NotExists)
                : await resiliencePolicy.ExecuteAsync(
                    "lock-acquire",
                    ExternalDependencyOperationKind.IdempotentWrite,
                    _ => database.StringSetAsync(key, ownerToken, leaseTime, When.NotExists),
                    IsTransient,
                    ct);
            if (acquired)
            {
                return new RedisLockHandle(database, key, ownerToken, leaseTime, resiliencePolicy);
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                return null;
            }

            await Task.Delay(retry, ct);
        }
        while (true);
    }

    private sealed class RedisLockHandle : IAsyncDisposable
    {
        private readonly IDatabase _database;
        private readonly RedisKey _key;
        private readonly RedisValue _ownerToken;
        private readonly TimeSpan _leaseTime;
        private readonly IExternalDependencyPolicy? _resiliencePolicy;
        private readonly CancellationTokenSource _renewalCancellation = new();
        private readonly Task _renewalTask;
        private int _released;

        public RedisLockHandle(
            IDatabase database,
            RedisKey key,
            RedisValue ownerToken,
            TimeSpan leaseTime,
            IExternalDependencyPolicy? resiliencePolicy)
        {
            _database = database;
            _key = key;
            _ownerToken = ownerToken;
            _leaseTime = leaseTime;
            _resiliencePolicy = resiliencePolicy;
            _renewalTask = RenewUntilReleasedAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            await _renewalCancellation.CancelAsync();
            try
            {
                await _renewalTask;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                try
                {
                    if (_resiliencePolicy is null)
                    {
                        await _database.ScriptEvaluateAsync(ReleaseScript, [_key], [_ownerToken]);
                    }
                    else
                    {
                        await _resiliencePolicy.ExecuteAsync(
                            "lock-release",
                            ExternalDependencyOperationKind.IdempotentWrite,
                            _ => _database.ScriptEvaluateAsync(ReleaseScript, [_key], [_ownerToken]),
                            IsTransient,
                            CancellationToken.None);
                    }
                }
                finally
                {
                    _renewalCancellation.Dispose();
                }
            }
        }

        private async Task RenewUntilReleasedAsync()
        {
            var renewalInterval = TimeSpan.FromMilliseconds(Math.Max(250, _leaseTime.TotalMilliseconds / 3));
            while (!_renewalCancellation.IsCancellationRequested)
            {
                await Task.Delay(renewalInterval, _renewalCancellation.Token);
                var renewed = _resiliencePolicy is null
                    ? await _database.ScriptEvaluateAsync(
                        RenewScript, [_key], [_ownerToken, (long)_leaseTime.TotalMilliseconds])
                    : await _resiliencePolicy.ExecuteAsync(
                        "lock-renew",
                        ExternalDependencyOperationKind.IdempotentWrite,
                        _ => _database.ScriptEvaluateAsync(
                            RenewScript, [_key], [_ownerToken, (long)_leaseTime.TotalMilliseconds]),
                        IsTransient,
                        _renewalCancellation.Token);
                if ((long)renewed == 0)
                {
                    throw new InvalidOperationException("Distributed lock ownership was lost before release.");
                }
            }
        }
    }

    private static bool IsTransient(Exception exception) => exception is RedisException;
}
