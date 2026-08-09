using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

public sealed class PreviewWorkItemRecurrenceHandler(WorkItemTemplateRecurrenceService service)
{
    private PreviewWorkItemRecurrenceSlice? slice;

    public PreviewWorkItemRecurrenceHandler(
        IDocumentRepository<WorkItemTemplateDocument> templates,
        IProjectPermissionChecker permissionChecker,
        ICurrentUser currentUser,
        IOptions<WorkItemRecurrenceOptions> options,
        IClock clock)
        : this(null!)
    {
        slice = new PreviewWorkItemRecurrenceSlice(
            templates,
            permissionChecker,
            currentUser,
            options,
            clock);
    }

    public Task<WorkItemRecurrencePreviewResponse> HandleAsync(
        PreviewWorkItemRecurrenceQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.PreviewRecurrenceAsync(query.Request, ct);
}
