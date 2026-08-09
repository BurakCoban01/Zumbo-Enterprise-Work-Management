using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Recurrences;

internal sealed class RecurrenceReadAccess(
    IDocumentRepository<WorkItemRecurrenceDocument> recurrences,
    IProjectPermissionChecker permissionChecker,
    ICurrentUser currentUser)
{
    internal async Task AuthorizeProjectAsync(string projectId, CancellationToken ct)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        _ = await permissionChecker.EnsureCanAsync(
            userId,
            projectId,
            PermissionCatalog.WorkItemView,
            ct);
    }

    internal async Task<WorkItemRecurrenceDocument> GetRecurrenceAsync(
        string recurrenceId,
        bool includeArchived,
        CancellationToken ct) =>
        await recurrences.SelectAsync(
            recurrence => recurrence.Id == recurrenceId
                && (includeArchived || !recurrence.Archived),
            ct)
        ?? throw new NotFoundException(
            "WORK_ITEM_RECURRENCE_NOT_FOUND",
            "Work item recurrence was not found.");
}
