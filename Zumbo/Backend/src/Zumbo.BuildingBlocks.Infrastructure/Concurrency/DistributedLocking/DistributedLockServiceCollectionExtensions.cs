using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
