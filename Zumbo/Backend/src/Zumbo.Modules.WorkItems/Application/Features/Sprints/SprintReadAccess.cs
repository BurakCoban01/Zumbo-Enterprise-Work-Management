using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

internal sealed class SprintReadAccess(
    IDocumentRepository<SprintDocument> sprints,
    IDocumentRepository<WorkItemDocument> workItems,
    IProjectPermissionChecker permissionChecker,
    ICurrentUser currentUser)
{
    internal async Task<SprintDocument> GetSprintAsync(string sprintId, CancellationToken ct) =>
        await sprints.SelectAsync(sprint => sprint.Id == sprintId, ct)
        ?? throw new NotFoundException("SPRINT_NOT_FOUND", "Sprint was not found.");

    internal async Task EnsureViewAsync(string projectId, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        _ = await permissionChecker.EnsureCanAsync(userId, projectId, PermissionCatalog.WorkItemView, ct);
    }

    internal Task<DocumentCursorPage<SprintDocument>> ListSprintsAsync(
        string projectId,
        string? after,
        int pageSize,
        CancellationToken ct) =>
        sprints.ListByCursorAsync(
            sprint => sprint.ProjectId == projectId,
            NormalizeOptional(after),
            Math.Clamp(pageSize, 1, 100),
            ct);

    internal Task<DocumentCursorPage<WorkItemDocument>> ListBacklogAsync(
        string projectId,
        string? after,
        int pageSize,
        CancellationToken ct) =>
        workItems.ListByCursorAsync(
            item => item.ProjectId == projectId && !item.Archived && item.SprintId == null,
            NormalizeOptional(after),
            Math.Clamp(pageSize, 1, 100),
            ct);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
