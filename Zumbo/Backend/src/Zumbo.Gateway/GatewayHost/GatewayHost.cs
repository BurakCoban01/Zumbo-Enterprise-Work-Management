using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms;

public static class GatewayHost
{
    public static void AddServices(WebApplicationBuilder builder)
    {
        GatewayConfigurationValidation.Validate(builder);
        builder.Services
            .AddOptions<GatewayOptions>()
            .Bind(builder.Configuration.GetSection("Gateway"))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<GatewayOptions>, GatewayOptionsValidator>();

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
        });
        builder.Services.AddCors(options => options.AddPolicy("Frontends", policy =>
        {
            var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            if (origins.Length > 0)
            {
                policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
            }
        }));
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("gateway", context =>
            {
                var limits = context.RequestServices.GetRequiredService<IOptions<GatewayOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.PermitLimit,
                        Window = TimeSpan.FromSeconds(limits.RateWindowSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
        });

        builder.Services.AddSingleton<IProxyConfigProvider, GatewayProxyConfigProvider>();
        builder.Services.TryAddSingleton<IForwarderHttpClientFactory, IdempotentRetryHttpClientFactory>();
        builder.Services.AddReverseProxy()
            .AddTransforms(context => context.AddRequestTransform(transform =>
            {
                var correlationId = transform.HttpContext.TraceIdentifier;
                transform.ProxyRequest.Headers.Host = transform.ProxyRequest.RequestUri?.Authority;
                transform.ProxyRequest.Headers.Remove("X-Correlation-Id");
                transform.ProxyRequest.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
                return ValueTask.CompletedTask;
            }));
        builder.Services.AddHttpClient<UpstreamReadinessProbe>(client => client.Timeout = Timeout.InfiniteTimeSpan);
        builder.Services.AddHealthChecks().AddCheck<UpstreamReadinessProbe>("upstream-api", tags: ["ready"]);
    }

    public static void ConfigurePipeline(WebApplication app)
    {
        var allowedOrigins = app.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        var allowedOriginSet = allowedOrigins.ToHashSet(StringComparer.Ordinal);

        app.UseForwardedHeaders();
        app.Use(async (context, next) =>
        {
            const string accessTokenParameter = "access_token";
            if (context.Request.Path.StartsWithSegments("/hubs")
                && context.Request.Query.TryGetValue(accessTokenParameter, out var values))
            {
                if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                if (!context.Request.Headers.ContainsKey("Authorization"))
                {
                    context.Request.Headers.Authorization = "Bearer " + values[0];
                }

                context.Request.QueryString = QueryString.Create(
                    context.Request.Query
                        .Where(entry => !entry.Key.Equals(accessTokenParameter, StringComparison.OrdinalIgnoreCase))
                        .SelectMany(entry => entry.Value, (entry, value) =>
                            new KeyValuePair<string, string?>(entry.Key, value)));
            }

            await next();
        });
        app.Use(async (context, next) =>
        {
            if (!context.Request.Headers.TryGetValue("X-Correlation-Id", out var supplied)
                || string.IsNullOrWhiteSpace(supplied)
                || supplied.ToString().Length > 128)
            {
                context.TraceIdentifier = Guid.NewGuid().ToString("N");
            }
            else
            {
                context.TraceIdentifier = supplied.ToString();
            }

            context.Response.Headers["X-Correlation-Id"] = context.TraceIdentifier;
            await next();
        });
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers["Cache-Control"] = "no-store, max-age=0";
                context.Response.Headers["Content-Security-Policy"] =
                    "default-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'; object-src 'none'";
                context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
                context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
                context.Response.Headers["Expires"] = "0";
                context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                context.Response.Headers["Pragma"] = "no-cache";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["X-Frame-Options"] = "DENY";
                if (!app.Environment.IsDevelopment())
                {
                    context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
                }

                return Task.CompletedTask;
            });
            await next();
        });
        app.Use(async (context, next) =>
        {
            var limits = context.RequestServices.GetRequiredService<IOptions<GatewayOptions>>().Value;
            if ((context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/hubs"))
                && context.Request.ContentLength > limits.MaxRequestBodyBytes)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }

            await next();
        });
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/hubs")
                && IsWebSocketUpgrade(context.Request)
                && (!context.Request.Headers.TryGetValue("Origin", out var origin)
                    || origin.Count != 1
                    || !allowedOriginSet.Contains(origin.ToString())))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next();
        });
        app.UseCors("Frontends");
        app.UseRateLimiter();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
        app.MapGet("/", () => Results.Ok(new { service = "Zumbo.Gateway", status = "ready" }));
        app.MapReverseProxy().RequireCors("Frontends").RequireRateLimiting("gateway");
    }

    private static bool IsWebSocketUpgrade(HttpRequest request) =>
        request.Headers.Upgrade.ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains("websocket", StringComparer.OrdinalIgnoreCase);
}
