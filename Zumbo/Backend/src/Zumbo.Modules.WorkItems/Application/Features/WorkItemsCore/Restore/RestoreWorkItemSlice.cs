using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class RestoreWorkItemSlice(
    IDocumentRepository<WorkItemDocument> workItems,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser,
    IProjectPermissionChecker permissionChecker,
    IBoardPlacementPolicy boardPlacementPolicy,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IWorkItemSearchPublisher searchPublisher,
    IWorkItemRealtimePublisher realtimePublisher,
    IWorkItemCacheInvalidationPublisher cacheInvalidationPublisher,
    IWorkItemActivityStore activityStore,
    IExpectedVersionAccessor? expectedVersions,
    WorkItemWipProjection? wipProjection,
    WorkItemRankService ranks,
    WorkItemCollaborationService? collaborationService)
{
    private readonly Dictionary<string, string> authorizedOrganizationIds = new(StringComparer.Ordinal);
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    internal async Task<WorkItemResponse> HandleAsync(
        RestoreWorkItemCommand command,
        CancellationToken ct)
    {
        var initialWorkItem = await GetArchivedWorkItemAsync(command.Id, ct);
        await using var structureLock = await AcquireRequiredLockAsync(
            "project-structure:" + initialWorkItem.ProjectId,
            ct);
        var workItem = await GetArchivedWorkItemAsync(command.Id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemDelete", ct);

        var placement = await boardPlacementPolicy.EnsureCanMoveAsync(
            workItem.ProjectId,
            workItem.BoardId,
            workItem.Id,
            workItem.Status,
            ct);
        await using (await AcquirePlacementLockAsync(workItem.BoardId, placement, ct))
        {
            if (wipProjection is null)
            {
                await boardPlacementPolicy.EnsureHasCapacityAsync(
                    workItem.BoardId,
                    placement.ColumnId,
                    workItem.Id,
                    ct);
            }
            else
            {
                await wipProjection.ReserveCreateAsync(
                    workItem.ProjectId,
                    workItem.BoardId,
                    placement,
                    ct);
            }

            workItem.ColumnId = placement.ColumnId;
            workItem.Status = placement.Status;
            workItem.Rank = await ranks.NextRankAsync(
                workItem.BoardId,
                placement.ColumnId,
                workItem.Id,
                ct);
            workItem.Archived = false;
            workItem.UpdatedAt = clock.UtcNow;
            await SaveAsync(workItem, ct);
        }

        await searchPublisher.IndexAsync(
            WorkItemPublicationMapper.ToSearchRecord(
                workItem,
                CurrentOrganizationId(workItem.ProjectId)),
            ct);
        await audit.WriteAsync(
            "WorkItemRestored",
            "WorkItem",
            workItem.Id,
            "archived",
            "active",
            command.CorrelationId,
            ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem,
            "WorkItemRestored",
            "Work item restored",
            command.CorrelationId,
            ct);
        await PublishRealtimeAsync("restored", workItem, command.CorrelationId, ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        return WorkItemResponseMapper.ToResponse(workItem);
    }

    private async Task<WorkItemDocument> GetArchivedWorkItemAsync(string id, CancellationToken ct)
    {
        var workItem = await workItems.SelectAsync(x => x.Id == id && x.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Archived work item was not found.");
        var authorization = await EnsurePermissionAsync(
            workItem.ProjectId,
            PermissionCatalog.WorkItemView,
            ct);
        await activityStore.HydrateAsync(workItem, authorization.OrganizationId, ct);
        return workItem;
    }

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

    private async Task<IAsyncDisposable?> AcquirePlacementLockAsync(
        string boardId,
        BoardPlacement placement,
        CancellationToken ct) =>
        placement.EnforcesWipLimit
            ? await AcquireRequiredLockAsync($"board-column:{boardId}:{placement.ColumnId}", ct)
            : null;

    private async Task<IAsyncDisposable> AcquireRequiredLockAsync(string resource, CancellationToken ct)
    {
        var options = distributedLockOptions.Value;
        var leaseTime = TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300));
        var waitTime = TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30));
        return await distributedLockProvider.TryAcquireAsync(resource, leaseTime, waitTime, ct)
            ?? throw new ConflictException(
                "RESOURCE_BUSY",
                "The requested resource is busy; retry the operation.");
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

    private Task PublishRealtimeAsync(
        string eventType,
        WorkItemDocument workItem,
        string correlationId,
        CancellationToken ct) =>
        realtimePublisher.PublishAsync(
            new WorkItemRealtimeChange(
                eventType,
                workItem.Id,
                workItem.ProjectId,
                workItem.BoardId,
                WorkItemPublicationMapper.ToRealtimeItem(workItem),
                correlationId,
                clock.UtcNow,
                WorkItemRealtimeProtocol.CurrentSchemaVersion,
                workItem.Version),
            ct);
}
