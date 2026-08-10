using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

public sealed class CreateWorkItemRecurrenceHandler(WorkItemTemplateRecurrenceService service)
{
    private CreateWorkItemRecurrenceSlice? slice;

    public CreateWorkItemRecurrenceHandler(
        IDocumentRepository<WorkItemTemplateDocument> templates,
        IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
        IDocumentRepository<WorkItemRecurrenceOccurrenceDocument> occurrences,
        IProjectPermissionChecker permissionChecker,
        ICurrentUser currentUser,
        IOptions<WorkItemRecurrenceOptions> options,
        IClock clock,
        IWorkItemAuditPublisher audit)
        : this(null!)
    {
        slice = new CreateWorkItemRecurrenceSlice(
            templates,
            recurrences,
            occurrences,
            permissionChecker,
            currentUser,
            options,
            clock,
            audit);
    }

    public Task<WorkItemRecurrenceResponse> HandleAsync(
        CreateWorkItemRecurrenceCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.CreateRecurrenceAsync(command.Request, command.CorrelationId, ct);
}
