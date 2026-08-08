using Zumbo.Modules.WorkItems;

namespace Zumbo.Modules.WorkItems.Application.Features.WorkItemsCore;

internal sealed record CreateWorkItemContext(
    string OrganizationId,
    string? RequestedId,
    string ActorUserId,
    string? IntakeSubmissionId,
    IReadOnlyCollection<StoredAttachment> InitialAttachments,
    string Description);
