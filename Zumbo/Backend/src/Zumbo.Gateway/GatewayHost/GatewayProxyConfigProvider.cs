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
