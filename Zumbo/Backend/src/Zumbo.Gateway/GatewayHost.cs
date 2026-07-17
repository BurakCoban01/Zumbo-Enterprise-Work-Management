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

internal static class GatewayConfigurationValidation
{
    internal static void Validate(WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        var options = configuration.GetSection("Gateway").Get<GatewayOptions>() ?? new GatewayOptions();
        options.Validate();

        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (!builder.Environment.IsDevelopment() && origins.Length == 0)
        {
            throw new InvalidOperationException("Cors:AllowedOrigins requires at least one exact origin outside Development.");
        }

        if (origins.Distinct(StringComparer.Ordinal).Count() != origins.Length)
        {
            throw new InvalidOperationException("Cors:AllowedOrigins must not contain duplicate origins.");
        }

        foreach (var origin in origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
                || !string.IsNullOrEmpty(uri.PathAndQuery.Trim('/'))
                || !string.IsNullOrEmpty(uri.Fragment)
                || origin.EndsWith('/'))
            {
                throw new InvalidOperationException($"Cors origin '{origin}' must be an exact HTTP(S) origin without a path or trailing slash.");
            }
        }

        if (builder.Environment.IsDevelopment())
        {
            return;
        }

        var hosts = (configuration["AllowedHosts"] ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hosts.Length == 0 || hosts.Contains("*", StringComparer.Ordinal))
        {
            throw new InvalidOperationException("AllowedHosts must contain explicit hosts and must not contain a wildcard outside Development.");
        }
    }
}

public sealed class GatewayOptions
{
    public string UpstreamBaseUrl { get; init; } = "http://127.0.0.1:58088/";
    public string[] UpstreamBaseUrls { get; init; } = [];
    public bool NotificationExtractionEnabled { get; init; }
    public string? NotificationUpstreamBaseUrl { get; init; }
    public string UpstreamReadyPath { get; init; } = "/health/ready";
    public string LoadBalancingPolicy { get; init; } = "RoundRobin";
    public int UpstreamTimeoutSeconds { get; init; } = 30;
    public long MaxRequestBodyBytes { get; init; } = 26 * 1024 * 1024;
    public int PermitLimit { get; init; } = 1000;
    public int RateWindowSeconds { get; init; } = 60;

    public void Validate()
    {
        var upstreams = ResolveUpstreamBaseUrls();
        if (upstreams.Count == 0
            || upstreams.Any(value => !Uri.TryCreate(value, UriKind.Absolute, out var upstream)
                || upstream.Scheme is not ("http" or "https")))
        {
            throw new InvalidOperationException("Gateway upstream base URLs must be absolute HTTP(S) URLs.");
        }

        if (upstreams.Distinct(StringComparer.OrdinalIgnoreCase).Count() != upstreams.Count)
        {
            throw new InvalidOperationException("Gateway upstream base URLs must be unique.");
        }

        if (NotificationExtractionEnabled
            && (string.IsNullOrWhiteSpace(NotificationUpstreamBaseUrl)
                || !Uri.TryCreate(NotificationUpstreamBaseUrl, UriKind.Absolute, out var notificationUpstream)
                || notificationUpstream.Scheme is not ("http" or "https")))
        {
            throw new InvalidOperationException(
                "Gateway:NotificationUpstreamBaseUrl must be an absolute HTTP(S) URL when notification extraction is enabled.");
        }

        if (LoadBalancingPolicy is not ("RoundRobin" or "PowerOfTwoChoices" or "LeastRequests" or "Random"))
        {
            throw new InvalidOperationException(
                "Gateway:LoadBalancingPolicy must be RoundRobin, PowerOfTwoChoices, LeastRequests, or Random.");
        }

        if (!UpstreamReadyPath.StartsWith("/", StringComparison.Ordinal)
            || UpstreamTimeoutSeconds <= 0
            || MaxRequestBodyBytes <= 0
            || PermitLimit <= 0
            || RateWindowSeconds <= 0)
        {
            throw new InvalidOperationException("Gateway numeric limits and readiness path must be valid.");
        }
    }

    public IReadOnlyList<string> ResolveUpstreamBaseUrls() =>
        UpstreamBaseUrls.Length > 0
            ? UpstreamBaseUrls.Select(NormalizeBaseUrl).ToArray()
            : [NormalizeBaseUrl(UpstreamBaseUrl)];

    public string? ResolveNotificationUpstreamBaseUrl() =>
        NotificationExtractionEnabled
            ? NormalizeBaseUrl(NotificationUpstreamBaseUrl!)
            : null;

    private static string NormalizeBaseUrl(string value) => value.Trim().TrimEnd('/') + "/";
}

public sealed class GatewayOptionsValidator : IValidateOptions<GatewayOptions>
{
    public ValidateOptionsResult Validate(string? name, GatewayOptions options)
    {
        try
        {
            options.Validate();
            return ValidateOptionsResult.Success;
        }
        catch (InvalidOperationException exception)
        {
            return ValidateOptionsResult.Fail(exception.Message);
        }
    }
}

public sealed class GatewayProxyConfigProvider : IProxyConfigProvider
{
    private readonly IProxyConfig _config;

    public GatewayProxyConfigProvider(IOptions<GatewayOptions> options)
    {
        var gateway = options.Value;
        var destinations = gateway.ResolveUpstreamBaseUrls()
            .Select((address, index) => new KeyValuePair<string, DestinationConfig>(
                $"replica-{index + 1}",
                new DestinationConfig { Address = address }))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        var routes = new List<RouteConfig>
        {
            new RouteConfig
            {
                RouteId = "api",
                ClusterId = "zumbo-api",
                Order = 0,
                MaxRequestBodySize = gateway.MaxRequestBodyBytes,
                Match = new RouteMatch { Path = "/api/{**catch-all}" }
            },
            new RouteConfig
            {
                RouteId = "realtime",
                ClusterId = "zumbo-api",
                Order = 0,
                MaxRequestBodySize = gateway.MaxRequestBodyBytes,
                Match = new RouteMatch { Path = "/hubs/{**catch-all}" }
            }
        };
        var clusters = new List<ClusterConfig>
        {
            CreateCluster("zumbo-api", gateway, destinations)
        };
        var notificationUpstream = gateway.ResolveNotificationUpstreamBaseUrl();
        if (notificationUpstream is not null)
        {
            routes.Insert(0, new RouteConfig
            {
                RouteId = "notifications-extraction",
                ClusterId = "zumbo-notifications",
                Order = -100,
                MaxRequestBodySize = gateway.MaxRequestBodyBytes,
                Match = new RouteMatch { Path = "/api/notifications/{**catch-all}" }
            });
            clusters.Add(CreateCluster(
                "zumbo-notifications",
                gateway,
                new Dictionary<string, DestinationConfig>(StringComparer.Ordinal)
                {
                    ["notifications-1"] = new() { Address = notificationUpstream }
                }));
        }

        _config = new GatewayProxyConfig(routes, clusters);
    }

    public IProxyConfig GetConfig() => _config;

    private static ClusterConfig CreateCluster(
        string clusterId,
        GatewayOptions gateway,
        IReadOnlyDictionary<string, DestinationConfig> destinations) => new()
    {
        ClusterId = clusterId,
        LoadBalancingPolicy = gateway.LoadBalancingPolicy,
        Destinations = destinations,
        HealthCheck = new HealthCheckConfig
        {
            Active = new ActiveHealthCheckConfig
            {
                Enabled = true,
                Interval = TimeSpan.FromSeconds(10),
                Timeout = TimeSpan.FromSeconds(gateway.UpstreamTimeoutSeconds),
                Policy = "ConsecutiveFailures",
                Path = gateway.UpstreamReadyPath
            }
        },
        HttpRequest = new ForwarderRequestConfig
        {
            ActivityTimeout = TimeSpan.FromSeconds(gateway.UpstreamTimeoutSeconds),
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        }
    };

    private sealed record GatewayProxyConfig(
        IReadOnlyList<RouteConfig> Routes,
        IReadOnlyList<ClusterConfig> Clusters) : IProxyConfig
    {
        public IChangeToken ChangeToken { get; } = new CancellationChangeToken(CancellationToken.None);
    }
}

public sealed class IdempotentRetryHttpClientFactory : ForwarderHttpClientFactory
{
    protected override HttpMessageHandler WrapHandler(
        ForwarderHttpClientContext context,
        HttpMessageHandler handler) => new IdempotentRetryHandler(handler);
}

internal sealed class IdempotentRetryHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    private static readonly HttpStatusCode[] RetryableStatusCodes =
    [
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    ];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!CanRetry(request))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        using var retryRequest = CloneWithoutBody(request);
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (!RetryableStatusCodes.Contains(response.StatusCode))
            {
                return response;
            }

            response.Dispose();
        }
        catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        return await base.SendAsync(retryRequest, cancellationToken);
    }

    private static bool CanRetry(HttpRequestMessage request) =>
        (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head)
        && request.Content is null
        && request.Headers.Upgrade.Count == 0;

    private static HttpRequestMessage CloneWithoutBody(HttpRequestMessage source)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri)
        {
            Version = source.Version,
            VersionPolicy = source.VersionPolicy
        };
        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in source.Options)
        {
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }

        return clone;
    }
}

public sealed class UpstreamReadinessProbe(HttpClient client, IOptions<GatewayOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var gateway = options.Value;
            var apiUpstreams = gateway.ResolveUpstreamBaseUrls();
            var healthy = await CountHealthyAsync(apiUpstreams, gateway, cancellationToken);
            if (healthy == 0)
            {
                return HealthCheckResult.Unhealthy("No upstream API replica is ready.");
            }

            var notificationUpstream = gateway.ResolveNotificationUpstreamBaseUrl();
            if (notificationUpstream is not null
                && await CountHealthyAsync([notificationUpstream], gateway, cancellationToken) == 0)
            {
                return HealthCheckResult.Unhealthy("The extracted notification upstream is not ready.");
            }

            return HealthCheckResult.Healthy(
                notificationUpstream is null
                    ? $"{healthy}/{apiUpstreams.Count} upstream replicas are ready."
                    : $"{healthy}/{apiUpstreams.Count} API replicas and the notification upstream are ready.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("Upstream API is unavailable.");
        }
    }

    private async Task<int> CountHealthyAsync(
        IReadOnlyCollection<string> baseUrls,
        GatewayOptions gateway,
        CancellationToken cancellationToken)
    {
        var healthy = 0;
        foreach (var baseUrl in baseUrls)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(gateway.UpstreamTimeoutSeconds));
            var endpoint = new Uri(new Uri(baseUrl, UriKind.Absolute), gateway.UpstreamReadyPath.TrimStart('/'));
            try
            {
                using var response = await client.GetAsync(endpoint, timeout.Token);
                if (response.IsSuccessStatusCode)
                {
                    healthy++;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
            }
        }
        return healthy;
    }
}
