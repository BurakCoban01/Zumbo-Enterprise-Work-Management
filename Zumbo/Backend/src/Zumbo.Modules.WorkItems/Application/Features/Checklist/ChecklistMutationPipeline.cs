using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class ChecklistMutationPipeline(
    IDocumentRepository<WorkItemDocument> workItems,
    IClock clock,
    ICurrentUser currentUser,
    IProjectPermissionChecker permissionChecker,
    IWorkItemActivityStore activityStore,
    IExpectedVersionAccessor? expectedVersions,
    WorkItemCollaborationService? collaborationService)
{
    private readonly Dictionary<string, string> authorizedOrganizationIds = new(StringComparer.Ordinal);
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    internal async Task<WorkItemDocument> LoadForUpdateAsync(string id, CancellationToken ct)
    {
        var workItem = await workItems.SelectAsync(x => x.Id == id && !x.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        var authorization = await EnsurePermissionAsync(
            workItem.ProjectId,
            PermissionCatalog.WorkItemView,
            ct);
        await activityStore.HydrateAsync(workItem, authorization.OrganizationId, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemUpdate", ct);
        return workItem;
    }

    internal async Task<WorkItemResponse> PersistAsync(
        WorkItemDocument workItem,
        string activityType,
        string activityDetail,
        string eventId,
        CancellationToken ct)
    {
        workItem.UpdatedAt = clock.UtcNow;
        return await PersistPreparedAsync(
            workItem,
            activityType,
            activityDetail,
            eventId,
            ct);
    }

    internal async Task<WorkItemResponse> PersistMutationAsync(
        WorkItemDocument workItem,
        string activityType,
        string activityDetail,
        string eventDiscriminator,
        CancellationToken ct)
    {
        workItem.UpdatedAt = clock.UtcNow;
        return await PersistPreparedAsync(
            workItem,
            activityType,
            activityDetail,
            MutationEventId(workItem, eventDiscriminator),
            ct);
    }

    private async Task<WorkItemResponse> PersistPreparedAsync(
        WorkItemDocument workItem,
        string activityType,
        string activityDetail,
        string eventId,
        CancellationToken ct)
    {
        await SaveAsync(workItem, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem,
            activityType,
            activityDetail,
            eventId,
            ct);
        return WorkItemResponseMapper.ToResponse(workItem);
    }

    private static string MutationEventId(WorkItemDocument workItem, string discriminator) =>
        $"{discriminator}:{workItem.UpdatedAt.ToUniversalTime().Ticks}";

    private async Task<ProjectResourceAuthorization> EnsurePermissionAsync(
        string projectId,
        string permission,
        CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        var authorization = await permissionChecker.EnsureCanAsync(userId, projectId, permission, ct);
        authorizedOrganizationIds[projectId] = authorization.OrganizationId;
        return authorization;
    }

    private string CurrentOrganizationId(string projectId)
    {
        if (!authorizedOrganizationIds.TryGetValue(projectId, out var organizationId))
        {
            throw new InvalidOperationException(
                "Project resource must be authorized before tenant data is accessed.");
        }

        return organizationId;
    }

    private async Task SaveAsync(WorkItemDocument workItem, CancellationToken ct)
    {
        await activityStore.MigrateEmbeddedAsync(
            workItem,
            CurrentOrganizationId(workItem.ProjectId),
            ct);
        var comments = workItem.Comments;
        var attachments = workItem.Attachments;
        var workLogs = workItem.WorkLogs;
        var approvals = workItem.Approvals;
        var statusHistory = workItem.StatusHistory;
        workItem.Comments = [];
        workItem.Attachments = [];
        workItem.WorkLogs = [];
        workItem.Approvals = [];
        workItem.StatusHistory = [];
        try
        {
            var result = await workItems.ReplaceByVersionAsync(
                x => x.Id == workItem.Id,
                workItem,
                expectedVersion.Consume(workItem.Version),
                ct);
            if (!result.Found)
            {
                throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
            }

            workItem.Version = result.Version!.Value;
        }
        finally
        {
            workItem.Comments = comments;
            workItem.Attachments = attachments;
            workItem.WorkLogs = workLogs;
            workItem.Approvals = approvals;
            workItem.StatusHistory = statusHistory;
        }
    }

    private async Task RecordActivityAndNotifyWatchersAsync(
        WorkItemDocument workItem,
        string activityType,
        string detail,
        string eventId,
        CancellationToken ct)
    {
        if (collaborationService is null)
        {
            return;
        }

        var organizationId = CurrentOrganizationId(workItem.ProjectId);
        await collaborationService.RecordActivityAsync(
            workItem,
            organizationId,
            activityType,
            detail,
            eventId,
            ct);
        await collaborationService.NotifyWatchersAsync(
            workItem,
            organizationId,
            "WatcherUpdate",
            $"{workItem.Title}: {detail}",
            eventId,
            null,
            ct);
    }
}
