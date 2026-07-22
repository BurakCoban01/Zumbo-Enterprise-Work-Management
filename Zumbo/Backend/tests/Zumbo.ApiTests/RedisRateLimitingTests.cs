using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Zumbo.ApiTests;

public sealed class RedisRateLimitingTests
{
    [Fact]
    public async Task DeniedRequest_UsesMostSpecificPolicyAndHashesPartition()
    {
        var counter = new StubCounter(new DistributedRateLimitResult(false, 0, TimeSpan.FromSeconds(17)));
        var nextCalled = false;
        var middleware = CreateMiddleware(counter, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = ContextWithPolicies("api", "bulk");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "sensitive-user-id")],
            "test"));

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal("bulk", counter.Policy);
        Assert.Equal(64, counter.PartitionHash?.Length);
        Assert.DoesNotContain("sensitive-user-id", counter.PartitionHash, StringComparison.Ordinal);
        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal("10", context.Response.Headers["RateLimit-Limit"]);
        Assert.Equal("0", context.Response.Headers["RateLimit-Remaining"]);
        Assert.Equal("17", context.Response.Headers.RetryAfter);
        context.Response.Body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("RATE_LIMIT_EXCEEDED", payload.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task AllowedRequest_ContinuesAndPublishesQuotaHeaders()
    {
        var counter = new StubCounter(new DistributedRateLimitResult(true, 28, TimeSpan.FromSeconds(42)));
        var nextCalled = false;
        var middleware = CreateMiddleware(counter, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = ContextWithPolicies("report");

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal("30", context.Response.Headers["RateLimit-Limit"]);
        Assert.Equal("28", context.Response.Headers["RateLimit-Remaining"]);
        Assert.Equal("42", context.Response.Headers["RateLimit-Reset"]);
    }

    [Fact]
    public async Task RedisTimeout_FailsOpenForReadPolicy()
    {
        var counter = new StubCounter(new TimeoutException());
        var nextCalled = false;
        var middleware = CreateMiddleware(counter, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = ContextWithPolicies("search");

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal("degraded-open", context.Response.Headers["X-Zumbo-RateLimit-State"]);
    }

    [Fact]
    public async Task RedisTimeout_FailsClosedForAuthenticationPolicy()
    {
        var counter = new StubCounter(new TimeoutException());
        var nextCalled = false;
        var middleware = CreateMiddleware(counter, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = ContextWithPolicies("api", "login");

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("degraded-closed", context.Response.Headers["X-Zumbo-RateLimit-State"]);
        context.Response.Body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("RATE_LIMIT_UNAVAILABLE", payload.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static RedisRateLimitingMiddleware CreateMiddleware(
        IDistributedRateLimitCounter counter,
        RequestDelegate next) =>
        new(
            next,
            counter,
            Options.Create(new RateLimitingOptions { Provider = "Redis" }),
            NullLogger<RedisRateLimitingMiddleware>.Instance);

    private static DefaultHttpContext ContextWithPolicies(params string[] policies)
    {
        var builder = new RouteEndpointBuilder(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/test"),
            order: 0);
        foreach (var policy in policies)
        {
            builder.Metadata.Add(new EnableRateLimitingAttribute(policy));
        }

        var context = new DefaultHttpContext
        {
            TraceIdentifier = "sec006-correlation"
        };
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.42");
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(builder.Build());
        return context;
    }

    private sealed class StubCounter : IDistributedRateLimitCounter
    {
        private readonly DistributedRateLimitResult? _result;
        private readonly Exception? _exception;

        public StubCounter(DistributedRateLimitResult result) => _result = result;
        public StubCounter(Exception exception) => _exception = exception;

        public string? Policy { get; private set; }
        public string? PartitionHash { get; private set; }

        public Task<DistributedRateLimitResult> IncrementAsync(
            string policy,
            string partitionHash,
            int permitLimit,
            TimeSpan window,
            CancellationToken cancellationToken)
        {
            Policy = policy;
            PartitionHash = partitionHash;
            return _exception is null
                ? Task.FromResult(_result!)
                : Task.FromException<DistributedRateLimitResult>(_exception);
        }
    }
}
