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
