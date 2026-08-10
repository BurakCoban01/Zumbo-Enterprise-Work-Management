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

namespace Zumbo.Api.Composition.Hosting.Registrars;

internal static class ApiHostOperationsRegistrar
{
    internal static void ConfigureBackgroundJobsAndHealth(WebApplicationBuilder builder, string provider, bool isWorkerRole)
{

        builder.Services.Configure<DurableEventProcessorOptions>(
            builder.Configuration.GetSection("DurableMessaging"));

        builder.Services.AddScoped<DurableEventProcessor>();

        if (isWorkerRole || builder.Configuration.GetValue("BackgroundJobs:Enabled", true))
        {
            builder.Services.AddHostedService<DurableEventWorker>();
            builder.Services.AddHostedService<AttachmentSecurityMaintenanceHostedService>();
        }


        var dependencyHealthTimeoutSeconds =
            builder.Configuration.GetValue("HealthChecks:DependencyTimeoutSeconds", 5);
        if (dependencyHealthTimeoutSeconds is < 1 or > 120)
        {
            throw new InvalidOperationException(
                "HealthChecks:DependencyTimeoutSeconds must be between 1 and 120.");
        }

        var dependencyHealthTimeout = TimeSpan.FromSeconds(dependencyHealthTimeoutSeconds);

        var healthChecks = builder.Services.AddHealthChecks()
            .AddCheck<StorageHealthCheck>("storage", timeout: dependencyHealthTimeout, tags: ["ready"])
            .AddCheck<ExternalDependencyPolicyHealthCheck>(
                "external_dependency_policies",
                timeout: dependencyHealthTimeout,
                tags: ["ready"])
            .AddCheck<DurableMessagingHealthCheck>(
                "durable_messaging",
                timeout: dependencyHealthTimeout,
                tags: ["ready"]);

        if (provider.Equals("Mongo", StringComparison.OrdinalIgnoreCase))
        {
            healthChecks.AddCheck<MongoHealthCheck>("mongodb", timeout: dependencyHealthTimeout, tags: ["ready"]);
        }
        else if (provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            healthChecks.AddCheck<PostgreSqlHealthCheck>("postgresql", timeout: dependencyHealthTimeout, tags: ["ready"]);
        }


        if ((builder.Configuration.GetValue<string>("DistributedLock:Provider") ?? "InMemory")
            .Equals("Redis", StringComparison.OrdinalIgnoreCase))
        {
            healthChecks.AddCheck<RedisHealthCheck>("redis", timeout: dependencyHealthTimeout, tags: ["ready"]);
        }

}
}
