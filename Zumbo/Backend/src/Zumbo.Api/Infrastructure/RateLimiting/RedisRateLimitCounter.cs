using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Zumbo.SharedKernel;

internal sealed class RedisRateLimitCounter(
    IConnectionMultiplexer connection,
    IOptions<RateLimitingOptions> options) : IDistributedRateLimitCounter
{
    private const string IncrementScript = """
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        local ttl = redis.call('PTTL', KEYS[1])
        if ttl < 0 then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
            ttl = tonumber(ARGV[1])
        end
        return { current, ttl }
        """;

    public async Task<DistributedRateLimitResult> IncrementAsync(
        string policy,
        string partitionHash,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        var settings = options.Value.Redis;
        var key = (RedisKey)$"{settings.KeyPrefix}{policy}:{partitionHash}";
        var operation = connection.GetDatabase().ScriptEvaluateAsync(
            IncrementScript,
            [key],
            [(RedisValue)(long)window.TotalMilliseconds]);
        var timeout = TimeSpan.FromMilliseconds(settings.OperationTimeoutMilliseconds);
        var raw = (RedisResult[])(await operation.WaitAsync(timeout, cancellationToken))!;
        var current = (long)raw[0];
        var retryAfterMilliseconds = Math.Max(1, (long)raw[1]);
        return new DistributedRateLimitResult(
            current <= permitLimit,
            Math.Max(0, permitLimit - current),
            TimeSpan.FromMilliseconds(retryAfterMilliseconds));
    }
}
