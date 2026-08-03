using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.BuildingBlocks.Infrastructure.Concurrency;

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
