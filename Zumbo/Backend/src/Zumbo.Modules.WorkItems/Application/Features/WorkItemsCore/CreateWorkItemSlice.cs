using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Application.Features.WorkItemsCore;
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
        CancellationToken ct,
        string? requestedId = null)
    {
        var authorization = await EnsurePermissionAsync(request.ProjectId, "WorkItemCreate", ct);
        return await HandleScopedAsync(
            request,
            correlationId,
            new CreateWorkItemContext(
                authorization.OrganizationId,
                requestedId,
                currentUser.UserId ?? "system",
                IntakeSubmissionId: null,
                InitialAttachments: [],
                Description: string.Empty),
            ct);
    }

    internal async Task<WorkItemResponse> HandleAsync(
        IntakeWorkItemCreation creation,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(creation.OrganizationId)
            || string.IsNullOrWhiteSpace(creation.SubmissionId))
        {
            throw new ValidationException("Intake work creation requires organization and submission scope.");
        }

        authorizedOrganizationIds[creation.Request.ProjectId] = creation.OrganizationId;
        var requestedId = IntakeStableIds.WorkItemId(creation.SubmissionId);
        var existing = await workItems.SelectAsync(x => x.Id == requestedId, ct);
        if (existing is not null)
        {
            if (existing.ProjectId != creation.Request.ProjectId
                || existing.SourceIntakeSubmissionId != creation.SubmissionId)
            {
                throw new ConflictException(
                    "INTAKE_WORK_ITEM_ID_CONFLICT",
                    "The intake submission work item id is already in use.");
            }

            await activityStore.HydrateAsync(existing, creation.OrganizationId, ct);
            return WorkItemResponseMapper.ToResponse(existing);
        }

        return await HandleScopedAsync(
            creation.Request,
            creation.CorrelationId,
            new CreateWorkItemContext(
                creation.OrganizationId,
                requestedId,
                currentUser.UserId ?? "intake",
                creation.SubmissionId,
                creation.Attachments,
                creation.Description),
            ct);
    }

    internal async Task<WorkItemResponse> HandleScopedAsync(
        CreateWorkItemRequest request,
        string correlationId,
        CreateWorkItemContext context,
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
            Id = string.IsNullOrWhiteSpace(context.RequestedId)
                ? Guid.NewGuid().ToString("N")
                : context.RequestedId,
            ProjectId = request.ProjectId,
            BoardId = request.BoardId,
            ParentId = parent?.Id,
            TeamId = teamId,
            ColumnId = placement.ColumnId,
            Title = request.Title.Trim(),
            Description = context.Description.Trim(),
            Type = type,
            IssueTypeSchemaVersion = shape.SchemaVersion,
            CustomFields = shape.CustomFields.ToList(),
            Priority = string.IsNullOrWhiteSpace(request.Priority) ? "Medium" : request.Priority,
            Status = placement.Status,
            Rank = rank,
            AssigneeUserId = request.AssigneeUserId,
            DueDate = request.DueDate,
            SourceIntakeSubmissionId = context.IntakeSubmissionId,
            CreatedAt = now,
            UpdatedAt = now,
            ActivityStorageVersion = 1,
            StatusHistory =
            [
                new WorkItemStatusHistoryDocument
                {
                    ToStatus = placement.Status,
                    ChangedByUserId = context.ActorUserId,
                    ChangedAt = now
                }
            ],
            Attachments = context.InitialAttachments.Select(stored => new AttachmentDocument
            {
                FileName = stored.FileName,
                ContentType = stored.ContentType,
                SizeBytes = stored.SizeBytes,
                StoragePath = stored.StoragePath,
                ChecksumSha256 = stored.ChecksumSha256,
                SecurityState = stored.SecurityState,
                ScanProvider = stored.ScanProvider,
                ScanDetail = stored.ScanDetail,
                ScannedAt = stored.ScannedAt,
                CreatedAt = now
            }).ToList()
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
            var separatedAttachments = workItem.Attachments;
            workItem.StatusHistory = [];
            workItem.Attachments = [];
            try
            {
                await workItems.CreateAsync(workItem, ct);
            }
            finally
            {
                workItem.StatusHistory = initialTimeline;
                workItem.Attachments = separatedAttachments;
            }

            await activityStore.CreateTimelineAsync(
                WorkItemActivityStore.ToActivity(
                    workItem,
                    context.OrganizationId,
                    workItem.StatusHistory[0],
                    0),
                ct);
            foreach (var attachment in workItem.Attachments)
            {
                await activityStore.CreateAttachmentAsync(
                    WorkItemActivityStore.ToActivity(workItem, context.OrganizationId, attachment),
                    ct);
            }
        }

        await searchPublisher.IndexAsync(
            WorkItemPublicationMapper.ToSearchRecord(workItem, context.OrganizationId),
            ct);
        await audit.WriteAsync(
            "WorkItemCreated", "WorkItem", workItem.Id, null, workItem.Title, correlationId, ct);
        if (collaborationService is not null)
        {
            await collaborationService.RecordActivityAsync(
                workItem,
                context.OrganizationId,
                "WorkItemCreated",
                "Work item created",
                correlationId,
                ct);
        }

        await PublishRealtimeAsync("created", workItem, correlationId, ct);
        if (!string.IsNullOrWhiteSpace(workItem.AssigneeUserId))
        {
            await notifications.NotifyWithSourceAsync(
                workItem.AssigneeUserId, "Assignment", $"Assigned to {workItem.Title}", ct,
                sourceId: workItem.Id, projectId: workItem.ProjectId);
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
