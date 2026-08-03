using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.BuildingBlocks.Infrastructure.Runtime;

public static class ExternalDependencyServiceCollectionExtensions
{
    public static IServiceCollection AddZumboExternalDependencyResilience(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        foreach (var dependency in ExternalDependencyNames.All)
        {
            var options = configuration.GetSection($"ExternalDependencies:{dependency}")
                .Get<ExternalDependencyPolicyOptions>() ?? new ExternalDependencyPolicyOptions();
            options.Validate(dependency);
        }
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ExternalDependencyTelemetry>();
        services.AddSingleton<IExternalDependencyJitter, RandomExternalDependencyJitter>();
        services.AddSingleton<IExternalDependencyPolicyProvider, ExternalDependencyPolicyProvider>();
        return services;
    }
}
