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
