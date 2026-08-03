using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed class CreateWorkItemSlice(
    IDocumentRepository<WorkItemDocument> workItems,
    IWorkItemNotificationPublisher notifications,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser,
    IProjectPermissionChecker permissionChecker,
    IWorkItemTeamPolicy teamPolicy,
    IBoardPlacementPolicy boardPlacementPolicy,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IWorkItemSearchPublisher searchPublisher,
    IWorkItemRealtimePublisher realtimePublisher,
    IWorkItemCacheInvalidationPublisher cacheInvalidationPublisher,
    IWorkItemActivityStore activityStore,
    WorkItemGraphService graph,
    WorkItemWipProjection? wipProjection,
    WorkItemRankService ranks,
    IWorkItemTypeSchemaPolicy typeSchemas,
    WorkItemCollaborationService? collaborationService,
    IWorkItemAutomationEventPublisher? automationEvents,
    IWorkItemAutomationChainContextAccessor? automationChain)
{
    private readonly Dictionary<string, string> authorizedOrganizationIds = new(StringComparer.Ordinal);

    internal async Task<WorkItemResponse> HandleAsync(
        CreateWorkItemRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var authorization = await EnsurePermissionAsync(request.ProjectId, "WorkItemCreate", ct);
        return await CreateAsync(
            request,
            authorization.OrganizationId,
            correlationId,
            currentUser.UserId ?? "system",
            ct);
    }

    private async Task<WorkItemResponse> CreateAsync(
        CreateWorkItemRequest request,
        string organizationId,
        string correlationId,
        string actorUserId,
        CancellationToken ct)
    {
        CreateWorkItemValidator.Validate(request);

        var shape = await typeSchemas.ValidateAsync(request.ProjectId, request.Type, request.CustomFields, ct);
        var type = shape.IssueTypeKey;
        await using var structureLock = await AcquireRequiredLockAsync("project-structure:" + request.ProjectId, ct);
        var parent = await ValidateParentAsync(request.ProjectId, request.BoardId, type, request.ParentId, ct);
        var teamId = NormalizeOptionalId(request.TeamId);
        if (teamId is not null)
        {
            await teamPolicy.EnsureCanAssignAsync(request.ProjectId, teamId, request.AssigneeUserId, ct);
        }

        var placement = await boardPlacementPolicy.ResolveInitialAsync(request.ProjectId, request.BoardId, ct);
        var rank = await ranks.NextRankAsync(request.BoardId, placement.ColumnId, null, ct);
        var now = clock.UtcNow;
        var workItem = new WorkItemDocument
        {
            Id = Guid.NewGuid().ToString("N"),
            ProjectId = request.ProjectId,
            BoardId = request.BoardId,
            ParentId = parent?.Id,
            TeamId = teamId,
            ColumnId = placement.ColumnId,
            Title = request.Title.Trim(),
            Description = string.Empty,
            Type = type,
            IssueTypeSchemaVersion = shape.SchemaVersion,
            CustomFields = shape.CustomFields.ToList(),
            Priority = string.IsNullOrWhiteSpace(request.Priority) ? "Medium" : request.Priority,
            Status = placement.Status,
            Rank = rank,
            AssigneeUserId = request.AssigneeUserId,
            DueDate = request.DueDate,
            CreatedAt = now,
            UpdatedAt = now,
            ActivityStorageVersion = 1,
            StatusHistory =
            [
                new WorkItemStatusHistoryDocument
                {
                    ToStatus = placement.Status,
                    ChangedByUserId = actorUserId,
                    ChangedAt = now
                }
            ]
        };

        await using (await AcquirePlacementLockAsync(request.BoardId, placement, ct))
        {
            if (wipProjection is null)
            {
                await boardPlacementPolicy.EnsureHasCapacityAsync(request.BoardId, placement.ColumnId, null, ct);
            }
            else
            {
                await wipProjection.ReserveCreateAsync(request.ProjectId, request.BoardId, placement, ct);
            }

            var initialTimeline = workItem.StatusHistory;
            workItem.StatusHistory = [];
            try
            {
                await workItems.CreateAsync(workItem, ct);
            }
            finally
            {
                workItem.StatusHistory = initialTimeline;
            }

            await activityStore.CreateTimelineAsync(
                WorkItemActivityStore.ToActivity(workItem, organizationId, workItem.StatusHistory[0], 0),
                ct);
        }

        await searchPublisher.IndexAsync(WorkItemPublicationMapper.ToSearchRecord(workItem, organizationId), ct);
        await audit.WriteAsync(
            "WorkItemCreated", "WorkItem", workItem.Id, null, workItem.Title, correlationId, ct);
        if (collaborationService is not null)
        {
            await collaborationService.RecordActivityAsync(
                workItem, organizationId, "WorkItemCreated", "Work item created", correlationId, ct);
        }

        await PublishRealtimeAsync("created", workItem, correlationId, ct);
        if (!string.IsNullOrWhiteSpace(workItem.AssigneeUserId))
        {
            await notifications.NotifyAsync(
                workItem.AssigneeUserId, "Assignment", $"Assigned to {workItem.Title}", ct);
        }

        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        await PublishAutomationAsync(
            "WorkItemCreated", workItem, null, correlationId, $"created:{workItem.Version}", ct);
        return WorkItemResponseMapper.ToResponse(workItem);
    }

    private async Task<WorkItemDocument?> ValidateParentAsync(
        string projectId,
        string boardId,
        string type,
        string? parentId,
        CancellationToken ct)
    {
        var hierarchyLevel = await typeSchemas.HierarchyLevelAsync(projectId, type, ct);
        if (string.IsNullOrWhiteSpace(parentId))
        {
            if (hierarchyLevel == IssueTypeHierarchyLevels.Subtask)
            {
                throw new ValidationException("A subtask must have a parent work item.");
            }

            return null;
        }

        if (hierarchyLevel == IssueTypeHierarchyLevels.Epic)
        {
            throw new ValidationException("An epic cannot have a parent work item.");
        }

        var parent = await GetWorkItemAsync(parentId, ct);
        if (!string.Equals(parent.ProjectId, projectId, StringComparison.Ordinal))
        {
            throw new ValidationException("A parent work item must belong to the same project.");
        }

        if (parent.CompletedAt is not null || IsCompletedStatus(parent.Status))
        {
            throw new ConflictException(
                "WORK_ITEM_PARENT_COMPLETED", "A completed work item cannot receive a child.");
        }

        var parentHierarchy = await typeSchemas.HierarchyLevelAsync(projectId, parent.Type, ct);
        if (hierarchyLevel == IssueTypeHierarchyLevels.Subtask)
        {
            if (parentHierarchy != IssueTypeHierarchyLevels.Standard)
            {
                throw new ValidationException("A subtask parent must be a story, task or bug.");
            }

            if (!string.Equals(parent.BoardId, boardId, StringComparison.Ordinal))
            {
                throw new ValidationException("A subtask and its parent must belong to the same board.");
            }
        }
        else if (parentHierarchy != IssueTypeHierarchyLevels.Epic)
        {
            throw new ValidationException("A story, task or bug can only be parented by an epic.");
        }

        await graph.EnsureCanSetParentAsync(projectId, null, parent.Id, ct);
        return parent;
    }

    private async Task<WorkItemDocument> GetWorkItemAsync(string id, CancellationToken ct)
    {
        var workItem = await workItems.SelectAsync(x => x.Id == id && !x.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        var authorization = await EnsurePermissionAsync(workItem.ProjectId, "WorkItemView", ct);
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
                "RESOURCE_BUSY", "The requested resource is busy; retry the operation.");
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

    private Task PublishAutomationAsync(
        string eventType,
        WorkItemDocument workItem,
        string? previousStatus,
        string correlationId,
        string mutationId,
        CancellationToken ct)
    {
        if (automationEvents is null)
        {
            return Task.CompletedTask;
        }

        var chain = automationChain?.Current;
        var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Status"] = workItem.Status,
            ["PreviousStatus"] = previousStatus,
            ["Priority"] = workItem.Priority,
            ["Type"] = workItem.Type,
            ["AssigneeUserId"] = workItem.AssigneeUserId,
            ["Labels"] = string.Join(',', workItem.Labels.Order(StringComparer.OrdinalIgnoreCase))
        };
        return automationEvents.PublishAsync(
            new WorkItemAutomationEvent(
                authorizedOrganizationIds[workItem.ProjectId],
                workItem.ProjectId,
                eventType,
                $"{workItem.Id}:{mutationId}",
                workItem.Id,
                currentUser.UserId ?? "system",
                correlationId,
                clock.UtcNow,
                fields,
                chain?.RootRunId,
                chain?.ChainDepth ?? 0,
                chain?.VisitedRuleIds ?? []),
            ct);
    }

    private static string? NormalizeOptionalId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsCompletedStatus(string status) =>
        status.Equals("Done", StringComparison.OrdinalIgnoreCase)
        || status.Equals("Closed", StringComparison.OrdinalIgnoreCase);
}
