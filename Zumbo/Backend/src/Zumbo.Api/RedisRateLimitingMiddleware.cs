using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Zumbo.SharedKernel;

internal sealed record DistributedRateLimitResult(
    bool IsAllowed,
    long Remaining,
    TimeSpan RetryAfter);

internal interface IDistributedRateLimitCounter
{
    Task<DistributedRateLimitResult> IncrementAsync(
        string policy,
        string partitionHash,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken);
}

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

internal sealed class RedisRateLimitingMiddleware(
    RequestDelegate next,
    IDistributedRateLimitCounter counter,
    IOptions<RateLimitingOptions> options,
    ILogger<RedisRateLimitingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var policyName = context.GetEndpoint()?.Metadata
            .GetOrderedMetadata<EnableRateLimitingAttribute>()
            .LastOrDefault()?.PolicyName;
        if (string.IsNullOrWhiteSpace(policyName))
        {
            await next(context);
            return;
        }

        var policy = options.Value.ResolvePolicy(policyName);
        DistributedRateLimitResult result;
        try
        {
            result = await counter.IncrementAsync(
                policy.Name,
                PartitionHash(context, policy.Name),
                policy.PermitLimit,
                TimeSpan.FromSeconds(policy.WindowSeconds),
                context.RequestAborted);
        }
        catch (TimeoutException)
        {
            await HandleUnavailableAsync(context, policy, "timeout");
            return;
        }
        catch (RedisException)
        {
            await HandleUnavailableAsync(context, policy, "redis-error");
            return;
        }

        ApplyRateLimitHeaders(context.Response, policy, result);
        if (result.IsAllowed)
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = RetryAfterSeconds(result.RetryAfter);
        context.Response.Headers["X-Correlation-Id"] = context.TraceIdentifier;
        await context.Response.WriteAsJsonAsync(
            ApiResponse<object>.Fail(
                "RATE_LIMIT_EXCEEDED",
                "Too many requests. Retry after the rate-limit window resets.",
                context.TraceIdentifier),
            context.RequestAborted);
    }

    private async Task HandleUnavailableAsync(
        HttpContext context,
        DistributedRateLimitPolicy policy,
        string reason)
    {
        logger.LogWarning(
            "Distributed rate limiter is unavailable for policy {Policy}; mode is {FailureMode} and reason is {Reason}.",
            policy.Name,
            policy.FailClosed ? "closed" : "open",
            reason);
        context.Response.Headers["X-Zumbo-RateLimit-State"] = policy.FailClosed
            ? "degraded-closed"
            : "degraded-open";

        if (!policy.FailClosed)
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = "1";
        context.Response.Headers["X-Correlation-Id"] = context.TraceIdentifier;
        await context.Response.WriteAsJsonAsync(
            ApiResponse<object>.Fail(
                "RATE_LIMIT_UNAVAILABLE",
                "Request protection is temporarily unavailable. Retry shortly.",
                context.TraceIdentifier),
            context.RequestAborted);
    }

    private static void ApplyRateLimitHeaders(
        HttpResponse response,
        DistributedRateLimitPolicy policy,
        DistributedRateLimitResult result)
    {
        response.Headers["RateLimit-Limit"] = policy.PermitLimit.ToString(CultureInfo.InvariantCulture);
        response.Headers["RateLimit-Remaining"] = result.Remaining.ToString(CultureInfo.InvariantCulture);
        response.Headers["RateLimit-Reset"] = RetryAfterSeconds(result.RetryAfter);
    }

    private static string RetryAfterSeconds(TimeSpan retryAfter) =>
        Math.Max(1, (long)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

    private static string PartitionHash(HttpContext context, string policyName)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var client = policyName is "login" or "password-reset" || string.IsNullOrWhiteSpace(userId)
            ? context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            : userId;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(client))).ToLowerInvariant();
    }
}
