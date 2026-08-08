using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Api.Composition.Modules.WorkItems;

internal static class WorkItemAttachmentComposition
{
    internal static IServiceCollection AddWorkItemAttachmentHandlers(this IServiceCollection services)
    {
        services.AddScoped<OpenAttachmentHandler>(provider => new OpenAttachmentHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetRequiredService<IAttachmentStorage>()));
        services.AddScoped<UploadAttachmentHandler>(provider => new UploadAttachmentHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IAttachmentStorage>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemCollaborationService>(),
            provider.GetService<ILogger<WorkItemService>>()));
        services.AddScoped<DeleteAttachmentHandler>(provider => new DeleteAttachmentHandler(
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IAttachmentStorage>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IWorkItemActivityStore>(),
            provider.GetService<IExpectedVersionAccessor>(),
            provider.GetService<WorkItemCollaborationService>(),
            provider.GetService<ILogger<WorkItemService>>()));
        return services;
    }
}
