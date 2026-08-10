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

internal static class ApiHostFoundationRegistrar
{
    internal static void ConfigureHostFoundation(WebApplicationBuilder builder)
    {
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));


        builder.Configuration
            .AddJsonFile("appsettings.Identity.json", optional: true)
            .AddJsonFile("appsettings.Organizations.json", optional: true)
            .AddJsonFile("appsettings.Teams.json", optional: true)
            .AddJsonFile("appsettings.Projects.json", optional: true)
            .AddJsonFile("appsettings.Boards.json", optional: true)
            .AddJsonFile("appsettings.WorkItems.json", optional: true)
            .AddJsonFile("appsettings.Workflows.json", optional: true)
            .AddEnvironmentVariables();


        ValidateRegistrationProvisioning(builder);

        ApiConfigurationValidation.Validate(builder);


        var requestLimits = builder.Configuration.GetSection("RequestLimits").Get<RequestLimitsOptions>()
            ?? new RequestLimitsOptions();

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = requestLimits.MaxRequestBodyBytes;
            options.Limits.MaxRequestHeaderCount = requestLimits.MaxHeaderCount;
            options.Limits.MaxRequestHeadersTotalSize = requestLimits.MaxHeaderBytes;
        });


        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddSwaggerGen();

        builder.Services.AddHttpContextAccessor();

        builder.Services.AddScoped<IExpectedVersionAccessor, HttpExpectedVersionAccessor>();

        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = requestLimits.MaxRequestBodyBytes;
            options.ValueLengthLimit = 16 * 1024;
            options.MultipartHeadersLengthLimit = 16 * 1024;
        });

        builder.Services.Configure<RequestLimitsOptions>(builder.Configuration.GetSection("RequestLimits"));

        builder.Services.AddZumboExternalDependencyResilience(builder.Configuration);

        builder.AddZumboObservability();

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            foreach (var value in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
            {
                if (IPAddress.TryParse(value, out var address))
                {
                    options.KnownProxies.Add(address);
                }
            }
        });

        builder.Services.AddCors(options =>
        {
            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            options.AddPolicy("LocalFrontends", policy =>
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .WithExposedHeaders(
                        "X-Zumbo-Report-Generated-At",
                        "X-Zumbo-Report-Source-Version",
                        "X-Zumbo-Report-Stale",
                        "X-Zumbo-Report-Age-Seconds",
                        "X-Zumbo-Export-Format")
                    .AllowCredentials());
        });

}

    internal static void ValidateRegistrationProvisioning(WebApplicationBuilder builder)
    {
        var mode = builder.Configuration["RegistrationProvisioning:Mode"]
            ?? RegistrationProvisioningModes.ProductionLike;
        if (mode.Equals(RegistrationProvisioningModes.LocalDemo, StringComparison.OrdinalIgnoreCase))
        {
            if (!builder.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "RegistrationProvisioning:Mode=LocalDemo is allowed only in Development.");
            }

            return;
        }

        if (!mode.Equals(RegistrationProvisioningModes.ProductionLike, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "RegistrationProvisioning:Mode must be ProductionLike or LocalDemo.");
        }
    }
}
