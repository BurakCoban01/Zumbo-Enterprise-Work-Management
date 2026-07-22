using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Zumbo.BuildingBlocks.Infrastructure.Concurrency;

namespace Zumbo.ApiTests;

public sealed class RedisScalingIntegrationTests
{
    [Fact]
    public async Task TwoIndependentClients_ShareRateCacheAndRenewableLease()
    {
        var connectionString = Environment.GetEnvironmentVariable("ZUMBO_REDIS_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var scope = "zumbo:ops002:" + Guid.NewGuid().ToString("N") + ":";
        await using var firstConnection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        await using var secondConnection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        try
        {
            await AssertSharedRateLimitAsync(firstConnection, secondConnection, scope);
            await AssertSharedCacheAsync(firstConnection, secondConnection, scope);
            await AssertSharedLeaseAsync(firstConnection, secondConnection, scope);
        }
        finally
        {
            await DeleteScopeKeysAsync(firstConnection, scope);
        }
    }

    private static async Task AssertSharedRateLimitAsync(
        IConnectionMultiplexer firstConnection,
        IConnectionMultiplexer secondConnection,
        string scope)
    {
        var settings = Options.Create(new RateLimitingOptions
        {
            Provider = "Redis",
            Redis = new RedisRateLimitingOptions
            {
                ConnectionString = "integration-test",
                KeyPrefix = scope + "rate:",
                OperationTimeoutMilliseconds = 2000
            }
        });
        var first = new RedisRateLimitCounter(firstConnection, settings);
        var second = new RedisRateLimitCounter(secondConnection, settings);
        var decisions = new List<DistributedRateLimitResult>();
        for (var index = 0; index < 6; index++)
        {
            var counter = index % 2 == 0 ? first : second;
            decisions.Add(await counter.IncrementAsync(
                "login",
                "shared-partition",
                permitLimit: 5,
                TimeSpan.FromMinutes(1),
                CancellationToken.None));
        }

        Assert.All(decisions.Take(5), decision => Assert.True(decision.IsAllowed));
        Assert.False(decisions[5].IsAllowed);
        Assert.Equal(0, decisions[5].Remaining);
    }

    private static async Task AssertSharedCacheAsync(
        IConnectionMultiplexer firstConnection,
        IConnectionMultiplexer secondConnection,
        string scope)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReadModelCache:KeyPrefix"] = scope + "cache:"
            })
            .Build();
        var first = new RedisWorkItemReadModelCache(
            firstConnection,
            configuration,
            NullLogger<RedisWorkItemReadModelCache>.Instance);
        var second = new RedisWorkItemReadModelCache(
            secondConnection,
            configuration,
            NullLogger<RedisWorkItemReadModelCache>.Instance);

        var firstValue = await first.GetOrCreateSnapshotAsync(
            "project-1",
            "summary",
            TimeSpan.FromMinutes(1),
            _ => Task.FromResult("from-node-1"),
            CancellationToken.None);
        var secondValue = await second.GetOrCreateSnapshotAsync(
            "project-1",
            "summary",
            TimeSpan.FromMinutes(1),
            _ => Task.FromResult("unexpected-node-2-factory"),
            CancellationToken.None);
        Assert.Equal("from-node-1", firstValue.Data);
        Assert.Equal("from-node-1", secondValue.Data);
        Assert.Equal(firstValue.GeneratedAt, secondValue.GeneratedAt);
        Assert.Equal(0, firstValue.SourceVersion);
        Assert.False(firstValue.Stale);

        await second.InvalidateProjectAsync("project-1", CancellationToken.None);
        var afterInvalidation = await first.GetOrCreateSnapshotAsync(
            "project-1",
            "summary",
            TimeSpan.FromMinutes(1),
            _ => Task.FromResult("after-invalidation"),
            CancellationToken.None);
        Assert.Equal("after-invalidation", afterInvalidation.Data);
        Assert.Equal(1, afterInvalidation.SourceVersion);
        Assert.False(afterInvalidation.Stale);
    }

    private static async Task AssertSharedLeaseAsync(
        IConnectionMultiplexer firstConnection,
        IConnectionMultiplexer secondConnection,
        string scope)
    {
        var settings = Options.Create(new RedisLockOptions
        {
            KeyPrefix = scope + "lock:",
            RetryMilliseconds = 20
        });
        var first = new RedisDistributedLockProvider(firstConnection, settings);
        var second = new RedisDistributedLockProvider(secondConnection, settings);

        await using (var firstLease = await first.TryAcquireAsync(
                         "shared-resource",
                         TimeSpan.FromMilliseconds(600),
                         TimeSpan.FromMilliseconds(100)))
        {
            Assert.NotNull(firstLease);
            await Task.Delay(850);
            Assert.Null(await second.TryAcquireAsync(
                "shared-resource",
                TimeSpan.FromMilliseconds(600),
                TimeSpan.FromMilliseconds(100)));
        }

        await using var secondLease = await second.TryAcquireAsync(
            "shared-resource",
            TimeSpan.FromMilliseconds(600),
            TimeSpan.FromMilliseconds(300));
        Assert.NotNull(secondLease);
    }

    private static async Task DeleteScopeKeysAsync(IConnectionMultiplexer connection, string scope)
    {
        var endpoint = connection.GetEndPoints().Single();
        var server = connection.GetServer(endpoint);
        var keys = server.Keys(pattern: scope + "*").ToArray();
        if (keys.Length > 0)
        {
            await connection.GetDatabase().KeyDeleteAsync(keys);
        }
    }
}
