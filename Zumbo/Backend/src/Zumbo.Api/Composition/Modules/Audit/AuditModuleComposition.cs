using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Audit;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Composition.Modules.Audit;

internal static class AuditModuleComposition
{
    internal static IServiceCollection AddAuditServices(this IServiceCollection services)
    {
        services.AddOptions<AuditOptions>()
            .BindConfiguration("Audit")
            .Validate(options => options.RetentionDays is >= 30 and <= 3650
                && options.ExportMaxRecords is >= 1 and <= 100_000
                && options.RetentionBatchSize is >= 1 and <= 200
                && options.IntegrityMaxRecords is >= 1 and <= 1_000_000
                && (!options.HashChainEnabled || System.Text.Encoding.UTF8.GetByteCount(options.IntegrityKey) >= 32),
                "Audit retention, export, integrity or hash-chain configuration is invalid.")
            .ValidateOnStart();
        services.AddScoped<AuditAccessCheckerAdapter>();
        services.AddScoped<IAuditAccessChecker>(provider => provider.GetRequiredService<AuditAccessCheckerAdapter>());
        services.AddScoped<IAuditTenantResolver>(provider => provider.GetRequiredService<AuditAccessCheckerAdapter>());
        services.AddScoped<IAuditRequestContext, HttpAuditRequestContext>();
        services.AddScoped<AuditService>();
        services.AddScoped<WriteAuditLogHandler>(provider => new WriteAuditLogHandler(
            provider.GetRequiredService<IDocumentRepository<AuditLogDocument>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IAuditRequestContext>(),
            provider.GetRequiredService<IOptions<AuditOptions>>(),
            provider.GetService<IAuditTenantResolver>(),
            provider.GetService<IDistributedLockProvider>()));
        services.AddScoped<QueryAuditLogHandler>(provider => new QueryAuditLogHandler(
            provider.GetRequiredService<IDocumentRepository<AuditLogDocument>>(),
            provider.GetRequiredService<IAuditAccessChecker>()));
        return services;
    }
}
