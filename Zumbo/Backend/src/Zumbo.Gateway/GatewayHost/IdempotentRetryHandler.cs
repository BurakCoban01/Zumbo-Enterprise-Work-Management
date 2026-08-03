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
