using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

public sealed class UpdateWorkItemTemplateHandler(WorkItemTemplateRecurrenceService service)
{
    private UpdateWorkItemTemplateSlice? slice;

    public UpdateWorkItemTemplateHandler(
        IDocumentRepository<WorkItemTemplateDocument> templates,
        IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
        IProjectPermissionChecker permissionChecker,
        ICurrentUser currentUser,
        IWorkItemTeamPolicy teamPolicy,
        IWorkItemCollaboratorDirectory collaboratorDirectory,
        IBoardPlacementPolicy boardPlacementPolicy,
        IWorkItemTypeSchemaPolicy typeSchemas,
        IDistributedLockProvider distributedLocks,
        IOptions<DistributedLockOptions> lockOptions,
        IClock clock,
        IWorkItemAuditPublisher audit,
        IExpectedVersionAccessor? expectedVersions = null)
        : this(null!)
    {
        slice = new UpdateWorkItemTemplateSlice(
            templates,
            recurrences,
            permissionChecker,
            currentUser,
            teamPolicy,
            collaboratorDirectory,
            boardPlacementPolicy,
            typeSchemas,
            distributedLocks,
            lockOptions,
            clock,
            audit,
            expectedVersions);
    }

    public Task<WorkItemTemplateResponse> HandleAsync(
        UpdateWorkItemTemplateCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.UpdateTemplateAsync(
            command.TemplateId,
            command.Request,
            command.CorrelationId,
            ct);
}
