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
