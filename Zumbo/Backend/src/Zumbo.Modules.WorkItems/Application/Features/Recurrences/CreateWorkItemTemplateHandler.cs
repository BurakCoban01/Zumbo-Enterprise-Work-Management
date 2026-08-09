using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

public sealed class CreateWorkItemTemplateHandler(WorkItemTemplateRecurrenceService service)
{
    private CreateWorkItemTemplateSlice? slice;

    public CreateWorkItemTemplateHandler(
        IDocumentRepository<WorkItemTemplateDocument> templates,
        IProjectPermissionChecker permissionChecker,
        ICurrentUser currentUser,
        IWorkItemTeamPolicy teamPolicy,
        IWorkItemCollaboratorDirectory collaboratorDirectory,
        IBoardPlacementPolicy boardPlacementPolicy,
        IWorkItemTypeSchemaPolicy typeSchemas,
        IDistributedLockProvider distributedLocks,
        IOptions<DistributedLockOptions> lockOptions,
        IClock clock,
        IWorkItemAuditPublisher audit)
        : this(null!)
    {
        slice = new CreateWorkItemTemplateSlice(
            templates,
            permissionChecker,
            currentUser,
            teamPolicy,
            collaboratorDirectory,
            boardPlacementPolicy,
            typeSchemas,
            distributedLocks,
            lockOptions,
            clock,
            audit);
    }

    public Task<WorkItemTemplateResponse> HandleAsync(
        CreateWorkItemTemplateCommand command,
        CancellationToken ct) =>
        slice?.HandleAsync(command, ct)
        ?? service.CreateTemplateAsync(command.Request, command.CorrelationId, ct);
}
