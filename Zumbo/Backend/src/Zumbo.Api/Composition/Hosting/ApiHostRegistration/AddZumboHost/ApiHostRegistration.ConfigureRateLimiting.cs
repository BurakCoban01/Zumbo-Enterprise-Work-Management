using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.BuildingBlocks.Infrastructure.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Messaging;
using Zumbo.BuildingBlocks.Infrastructure.Runtime;
using Zumbo.BuildingBlocks.Infrastructure.Search;
using Zumbo.BuildingBlocks.Infrastructure.Security;
using Zumbo.BuildingBlocks.Infrastructure.Storage;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.WorkItems;
using Zumbo.Persistence.PostgreSql;
using Zumbo.SharedKernel;
using MongoDurableTransactionRunner = Zumbo.BuildingBlocks.Infrastructure.Persistence.MongoDurableTransactionRunner;
using MongoTransactionContext = Zumbo.BuildingBlocks.Infrastructure.Persistence.MongoTransactionContext;

internal static partial class ApiHostRegistration
{
private static void ConfigureRateLimiting(WebApplicationBuilder builder)
{
        var rateLimits = builder.Configuration.GetSection("RateLimiting").Get<RateLimitingOptions>() ?? new RateLimitingOptions();

        builder.Services.Configure<RateLimitingOptions>(builder.Configuration.GetSection("RateLimiting"));

        builder.Services.AddZumboDistributedLocking(builder.Configuration);

        if (rateLimits.Provider.Equals("Redis", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<IDistributedRateLimitCounter, RedisRateLimitCounter>();
        }

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                var http = context.HttpContext;
                http.Response.Headers["X-Correlation-Id"] = http.TraceIdentifier;
                await http.Response.WriteAsJsonAsync(
                    ApiResponse<object>.Fail(
                        "RATE_LIMIT_EXCEEDED",
                        "Too many requests. Retry after the rate-limit window resets.",
                        http.TraceIdentifier),
                    cancellationToken);
            };
            options.AddPolicy("login", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Clamp(rateLimits.LoginPermitLimit, 3, 1000),
                        Window = TimeSpan.FromSeconds(rateLimits.StandardWindowSeconds),
                        QueueLimit = 0
                    }));
            options.AddPolicy("password-reset", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Clamp(rateLimits.PasswordResetPermitLimit, 1, 100),
                        Window = TimeSpan.FromSeconds(rateLimits.PasswordResetWindowSeconds),
                        QueueLimit = 0
                    }));
            options.AddPolicy("api", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? context.Connection.RemoteIpAddress?.ToString()
                        ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Clamp(rateLimits.ApiPermitLimit, 30, 10_000),
                        Window = TimeSpan.FromSeconds(rateLimits.StandardWindowSeconds),
                        QueueLimit = 0
                    }));
            options.AddPolicy("search", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? context.Connection.RemoteIpAddress?.ToString()
                        ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Clamp(rateLimits.SearchPermitLimit, 10, 5_000),
                        Window = TimeSpan.FromSeconds(rateLimits.StandardWindowSeconds),
                        QueueLimit = 0
                    }));
            options.AddPolicy("upload", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? context.Connection.RemoteIpAddress?.ToString()
                        ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Clamp(rateLimits.UploadPermitLimit, 1, 1_000),
                        Window = TimeSpan.FromSeconds(rateLimits.StandardWindowSeconds),
                        QueueLimit = 0
                    }));
            options.AddPolicy("intake-public", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Clamp(rateLimits.IntakePublicPermitLimit, 1, 1_000),
                        Window = TimeSpan.FromSeconds(rateLimits.StandardWindowSeconds),
                        QueueLimit = 0
                    }));
            options.AddPolicy("report", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? context.Connection.RemoteIpAddress?.ToString()
                        ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Clamp(rateLimits.ReportPermitLimit, 1, 5_000),
                        Window = TimeSpan.FromSeconds(rateLimits.StandardWindowSeconds),
                        QueueLimit = 0
                    }));
            options.AddPolicy("bulk", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? context.Connection.RemoteIpAddress?.ToString()
                        ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Clamp(rateLimits.BulkPermitLimit, 1, 1_000),
                        Window = TimeSpan.FromSeconds(rateLimits.StandardWindowSeconds),
                        QueueLimit = 0
                    }));
            options.AddPolicy("realtime-connect", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? context.Connection.RemoteIpAddress?.ToString()
                        ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Clamp(rateLimits.RealtimeConnectPermitLimit, 10, 10_000),
                        Window = TimeSpan.FromSeconds(rateLimits.StandardWindowSeconds),
                        QueueLimit = 0
                    }));
        });

}}
