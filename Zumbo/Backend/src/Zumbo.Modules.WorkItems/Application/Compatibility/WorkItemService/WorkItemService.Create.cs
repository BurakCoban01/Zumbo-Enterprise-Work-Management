using Zumbo.Modules.WorkItems.Application.Features.WorkItemsCore;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemService
{
    public async Task<WorkItemResponse> CreateAsync(
        CreateWorkItemRequest request,
        string correlationId,
        CancellationToken ct,
        string? requestedId = null)
        => await createWorkItemHandler.HandleAsync(request, correlationId, ct, requestedId);

    async Task<WorkItemResponse> IIntakeWorkItemCreator.CreateAsync(
        IntakeWorkItemCreation creation,
        CancellationToken ct)
        => await createWorkItemHandler.CreateAsync(creation, ct);

    private async Task<WorkItemResponse> CreateCoreAsync(
        CreateWorkItemRequest request,
        string organizationId,
        string correlationId,
        CancellationToken ct,
        string? requestedId,
        string actorUserId,
        string? intakeSubmissionId,
        IReadOnlyCollection<StoredAttachment> initialAttachments,
        string description = "")
        => await createWorkItemHandler.HandleScopedAsync(
            request,
            correlationId,
            new CreateWorkItemContext(
                organizationId,
                requestedId,
                actorUserId,
                intakeSubmissionId,
                initialAttachments,
                description),
            ct);
}
