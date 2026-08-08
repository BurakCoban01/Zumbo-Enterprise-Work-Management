using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Workflows;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Composition.Modules.Workflows;

internal static class WorkflowModuleComposition
{
    internal static IServiceCollection AddWorkflowServices(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        services.AddScoped<IWorkflowProjectAccessChecker, WorkflowProjectAccessCheckerAdapter>();
        services.AddScoped<IWorkflowAuditWriter, WorkflowAuditWriterAdapter>();
        services.AddScoped<IWorkflowPublicationGuard, WorkflowPublicationGuardAdapter>();
        services.AddScoped<WorkflowService>();
        services.AddScoped<UpsertWorkflowHandler>(provider => new UpsertWorkflowHandler(
            provider.GetRequiredService<IDocumentRepository<WorkflowDefinitionDocument>>(),
            provider.GetRequiredService<IWorkflowProjectAccessChecker>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<IWorkflowAuditWriter>(),
            provider.GetRequiredService<IExpectedVersionAccessor>(),
            provider.GetRequiredService<IWorkflowPublicationGuard>()));
        services.AddScoped<SaveWorkflowDraftHandler>();
        services.AddScoped<PublishWorkflowHandler>();
        services.AddScoped<GetWorkflowHandler>(provider => new GetWorkflowHandler(
            provider.GetRequiredService<IDocumentRepository<WorkflowDefinitionDocument>>(),
            provider.GetRequiredService<IWorkflowProjectAccessChecker>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<IExpectedVersionAccessor>()));
        return services.AddAutomationEngine(configuration);
    }
}
