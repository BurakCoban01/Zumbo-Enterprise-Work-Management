using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record UpdateWorkItemRequest(string? Title, string? Description, string? Priority, DateTimeOffset? DueDate);
public sealed record AssignWorkItemRequest(string AssigneeUserId);
public sealed record MoveWorkItemRequest(string Status);
public sealed record ReorderWorkItemRequest(string? BeforeWorkItemId, string? AfterWorkItemId);
public sealed record SetWorkItemPlanningRequest(string? SprintId, decimal? EstimatePoints);
public sealed record SetWorkItemCustomFieldsRequest(IReadOnlyCollection<WorkItemCustomFieldValueRequest> Values);
public sealed record AddChecklistItemRequest(string Text);
public sealed record CompleteChecklistItemRequest(bool Completed);
public sealed record AddLabelRequest(string Label);
public sealed record AddCommentRequest(string Body, IReadOnlyCollection<string>? Mentions);
public sealed record EditCommentRequest(string Body);
public sealed record AddWorkLogRequest(string UserId, decimal Hours, string? Note);
public sealed record LinkWorkItemRequest(string RelatedWorkItemId, string RelationType);
public sealed record SetWorkItemParentRequest(string? ParentId);
public sealed record SetWorkItemTeamRequest(string? TeamId);
public sealed record RequestWorkItemApprovalRequest(string TargetStatus);
public sealed record DecideWorkItemApprovalRequest(bool Approved, string? Note);
public sealed record BulkMoveWorkItemsRequest(IReadOnlyCollection<string> WorkItemIds, string Status);
public sealed record BulkAssignWorkItemsRequest(IReadOnlyCollection<string> WorkItemIds, string AssigneeUserId);
public sealed record BulkArchiveWorkItemsRequest(IReadOnlyCollection<string> WorkItemIds);
public sealed record BulkWorkItemResult(string WorkItemId, bool Success, string? ErrorCode, string? ErrorMessage);
public sealed record BulkWorkItemResponse(IReadOnlyCollection<BulkWorkItemResult> Results, int Succeeded, int Failed);

public sealed record AttachmentFile(Stream Content, string FileName, string ContentType, long SizeBytes);
public sealed record ProjectSummaryResponse(int Total, int Done, int InProgress, int Overdue);
public sealed record StatusDistributionResponse(string Status, int Count);
public sealed record UserWorkloadResponse(string UserId, int OpenItems, int OverdueItems, decimal LoggedHours);
public sealed record DueDateRiskResponse(string Id, string Title, string? AssigneeUserId, DateTimeOffset DueDate, string Status);
public sealed record SprintBurndownPointResponse(DateOnly Date, decimal RemainingPoints, int RemainingItems);
public sealed record SprintVelocityResponse(string SprintId, int CompletedItems, decimal CompletedPoints);
public sealed record FlowTimeReportResponse(
    DateOnly From,
    DateOnly To,
    int CompletedItems,
    int CycleTimeSampleSize,
    double AverageLeadTimeHours,
    double MedianLeadTimeHours,
    double? AverageCycleTimeHours,
    double? MedianCycleTimeHours);
public sealed record TaskCompletionRateResponse(
    DateOnly From,
    DateOnly To,
    int CreatedItems,
    int CompletedItems,
    double CompletionRatePercent);
public sealed record TeamPerformanceResponse(
    string TeamId,
    string TeamName,
    int AssignedItems,
    int CompletedItems,
    double CompletionRatePercent,
    double? AverageLeadTimeHours,
    decimal LoggedHours);
public sealed class DueDateReminderOptions
{
    public bool Enabled { get; init; } = true;
    public int HorizonHours { get; init; } = 24;
    public int IntervalMinutes { get; init; } = 15;
}
public sealed record WorkflowTransitionRule(
    string FromStatus,
    string ToStatus,
    bool RequiresAssignee,
    bool RequiresCompletedChecklist,
    bool RequiresApproval = false,
    IReadOnlyCollection<WorkflowAutomationRule>? Automations = null,
    string ToStatusCategory = "InProgress");
public sealed record WorkflowAutomationRule(string Action, string? Value);

public interface IProjectPermissionChecker
{
    Task<ProjectResourceAuthorization> EnsureCanAsync(
        string userId,
        string projectId,
        string permission,
        CancellationToken ct);
}

public sealed record WorkItemTeamEntry(string Id, string Name);

public interface IWorkItemTeamPolicy
{
    Task EnsureCanAssignAsync(
        string projectId,
        string teamId,
        string? assigneeUserId,
        CancellationToken ct);
    Task<IReadOnlyCollection<WorkItemTeamEntry>> ListProjectTeamsAsync(string projectId, CancellationToken ct);
}

public interface IWorkflowPolicy
{
    Task<WorkflowTransitionRule> EnsureTransitionAllowedAsync(
        string projectId,
        string issueType,
        string fromStatus,
        string toStatus,
        CancellationToken ct);
}

public sealed record BoardPlacement(string ColumnId, string Status, bool EnforcesWipLimit, int? WipLimit = null);

public interface IBoardPlacementPolicy
{
    Task<BoardPlacement> ResolveInitialAsync(string projectId, string boardId, CancellationToken ct);
    Task<BoardPlacement> EnsureCanMoveAsync(
        string projectId,
        string boardId,
        string workItemId,
        string targetStatus,
        CancellationToken ct);
    Task EnsureHasCapacityAsync(
        string boardId,
        string columnId,
        string? ignoredWorkItemId,
        CancellationToken ct);
}

public sealed partial class WorkItemService(
    IDocumentRepository<WorkItemDocument> workItems,
    IWorkItemNotificationPublisher notifications,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser,
    IProjectPermissionChecker permissionChecker,
    IWorkItemTeamPolicy teamPolicy,
    IWorkflowPolicy workflowPolicy,
    IBoardPlacementPolicy boardPlacementPolicy,
    IAttachmentStorage attachmentStorage,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IWorkItemSearchIndex searchIndex,
    IWorkItemSearchPublisher searchPublisher,
    IWorkItemRealtimePublisher realtimePublisher,
    IWorkItemReadModelCache readModelCache,
    IWorkItemCacheInvalidationPublisher cacheInvalidationPublisher,
    IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions,
    IWorkItemActivityStore activityStore,
    WorkItemGraphService graph,
    IExpectedVersionAccessor? expectedVersions = null,
    WorkItemWipProjection? wipProjection = null,
    WorkItemRankService? rankService = null,
    IWorkItemSprintPolicy? sprintPolicy = null,
    IWorkItemTypeSchemaPolicy? typeSchemaPolicy = null,
    WorkItemCollaborationService? collaborationService = null,
    IOptions<SearchOptions>? searchOptions = null)
{
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);
    private readonly Dictionary<string, string> authorizedOrganizationIds = new(StringComparer.Ordinal);
    private readonly WorkItemRankService ranks = rankService ?? new(workItems, clock, Options.Create(new WorkItemRankOptions()));
    private readonly IWorkItemTypeSchemaPolicy typeSchemas = typeSchemaPolicy ?? new LegacyWorkItemTypeSchemaPolicy();
    private readonly SearchOptions searchRuntimeOptions = searchOptions?.Value ?? new SearchOptions();
    public async Task<WorkItemResponse> CreateAsync(
        CreateWorkItemRequest request,
        string correlationId,
        CancellationToken ct,
        string? requestedId = null)
    {
        var authorization = await EnsurePermissionAsync(request.ProjectId, "WorkItemCreate", ct);

        CreateWorkItemValidator.Validate(request);

        var shape = await typeSchemas.ValidateAsync(request.ProjectId, request.Type, request.CustomFields, ct);
        var type = shape.IssueTypeKey;
        await using var structureLock = await AcquireRequiredLockAsync("project-structure:" + request.ProjectId, ct);
        var parent = await ValidateParentAsync(request.ProjectId, request.BoardId, type, request.ParentId, null, ct);
        var teamId = NormalizeOptionalId(request.TeamId);
        if (teamId is not null)
        {
            await teamPolicy.EnsureCanAssignAsync(request.ProjectId, teamId, request.AssigneeUserId, ct);
        }
        var placement = await boardPlacementPolicy.ResolveInitialAsync(request.ProjectId, request.BoardId, ct);
        var rank = await ranks.NextRankAsync(request.BoardId, placement.ColumnId, null, ct);
        var now = clock.UtcNow;
        var organizationId = authorization.OrganizationId;
        var workItem = new WorkItemDocument
        {
            Id = string.IsNullOrWhiteSpace(requestedId) ? Guid.NewGuid().ToString("N") : requestedId,
            ProjectId = request.ProjectId,
            BoardId = request.BoardId,
            ParentId = parent?.Id,
            TeamId = teamId,
            ColumnId = placement.ColumnId,
            Title = request.Title.Trim(),
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
                    ChangedByUserId = currentUser.UserId ?? "system",
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
        await searchPublisher.IndexAsync(ToScopedSearchRecord(workItem), ct);
        await audit.WriteAsync("WorkItemCreated", "WorkItem", workItem.Id, null, workItem.Title, correlationId, ct);
        if (collaborationService is not null)
        {
            await collaborationService.RecordActivityAsync(
                workItem, organizationId, "WorkItemCreated", "Work item created", correlationId, ct);
        }
        await PublishRealtimeAsync("created", workItem, correlationId, ct);

        if (!string.IsNullOrWhiteSpace(workItem.AssigneeUserId))
        {
            await notifications.NotifyAsync(workItem.AssigneeUserId, "Assignment", $"Assigned to {workItem.Title}", ct);
        }

        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        return ToResponse(workItem);
    }

    public async Task<IReadOnlyList<WorkItemResponse>> SearchAsync(WorkItemSearchRequest request, CancellationToken ct) =>
        (await SearchPageAsync(request, ct)).Items;

    public async Task<WorkItemSearchPageResponse> SearchPageAsync(WorkItemSearchRequest request, CancellationToken ct)
    {
        SearchWorkItemsValidator.Validate(request);
        await EnsurePermissionAsync(request.ProjectId!, "WorkItemView", ct);
        var searchFilter = await typeSchemas.ValidateSearchFilterAsync(
            request.ProjectId!,
            request.IssueType,
            request.CustomFieldKey,
            request.CustomFieldValue,
            ct);
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var text = request.Text?.Trim().ToLowerInvariant();

        if (!request.Archived && (!string.IsNullOrWhiteSpace(text)
            || !string.IsNullOrWhiteSpace(searchFilter.IssueType)
            || !string.IsNullOrWhiteSpace(searchFilter.CustomFieldKey)))
        {
            WorkItemSearchResult searchResult;
            var query = new WorkItemSearchQuery(
                    CurrentOrganizationId(request.ProjectId!),
                    request.ProjectId!,
                    text,
                    request.AssigneeUserId,
                    request.Status,
                    page,
                    pageSize,
                    searchFilter.IssueType,
                    searchFilter.CustomFieldKey,
                    searchFilter.CustomFieldValue);
            try
            {
                searchResult = await searchIndex.SearchAsync(query, ct);
            }
            catch (WorkItemSearchUnavailableException)
            {
                return await SearchDegradedAsync(request, searchFilter, text, page, pageSize, ct);
            }
            var ids = searchResult.Ids;

            if (ids.Count == 0)
            {
                return new WorkItemSearchPageResponse([], searchResult.TotalCount, false);
            }

            var idSet = ids.ToHashSet(StringComparer.Ordinal);
            var indexedResult = await workItems.ListByFilterAsync(
                x => !x.Archived && x.ProjectId == request.ProjectId && idSet.Contains(x.Id),
                pageSize: 200,
                cancellationToken: ct);

            var resultById = indexedResult.ToDictionary(x => x.Id, StringComparer.Ordinal);
            await HydrateAllAsync(resultById.Values, ct);
            var items = ids
                .Where(resultById.ContainsKey)
                .Select(id => ToResponse(resultById[id]))
                .ToList();
            return new WorkItemSearchPageResponse(items, searchResult.TotalCount, false);
        }

        var result = await workItems.ListByFilterAsync(
            x => x.Archived == request.Archived
                && x.ProjectId == request.ProjectId
                && (string.IsNullOrEmpty(request.AssigneeUserId) || x.AssigneeUserId == request.AssigneeUserId)
                && (string.IsNullOrEmpty(request.Status) || x.Status == request.Status)
                && (string.IsNullOrEmpty(request.IssueType) || x.Type == request.IssueType)
                && (string.IsNullOrEmpty(text) || x.Title.ToLower().Contains(text) || x.Description.ToLower().Contains(text)),
            x => x.Rank,
            page: page,
            pageSize: pageSize,
            cancellationToken: ct);
        var totalCount = await workItems.CountByFilterAsync(
            x => x.Archived == request.Archived
                && x.ProjectId == request.ProjectId
                && (string.IsNullOrEmpty(request.AssigneeUserId) || x.AssigneeUserId == request.AssigneeUserId)
                && (string.IsNullOrEmpty(request.Status) || x.Status == request.Status)
                && (string.IsNullOrEmpty(request.IssueType) || x.Type == request.IssueType)
                && (string.IsNullOrEmpty(text) || x.Title.ToLower().Contains(text) || x.Description.ToLower().Contains(text)),
            ct);

        await HydrateAllAsync(result, ct);
        return new WorkItemSearchPageResponse(result.Select(ToResponse).ToList(), totalCount, false);
    }

    public async Task<WorkItemResponse> GetAsync(string id, CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemView", ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> UpdateAsync(string id, UpdateWorkItemRequest request, string correlationId, CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemUpdate", ct);
        var oldValue = $"{workItem.Title}|{workItem.Priority}|{workItem.DueDate:o}";

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            if (request.Title.Length > 200)
            {
                throw new ValidationException("Work item title cannot exceed 200 characters.");
            }

            workItem.Title = request.Title.Trim();
        }

        if (request.Description is not null)
        {
            workItem.Description = request.Description.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Priority))
        {
            workItem.Priority = request.Priority.Trim();
        }

        if (workItem.DueDate != request.DueDate)
        {
            workItem.DueReminderSentAt = null;
        }
        workItem.DueDate = request.DueDate;
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await searchPublisher.IndexAsync(ToScopedSearchRecord(workItem), ct);
        await audit.WriteAsync("WorkItemUpdated", "WorkItem", workItem.Id, oldValue, $"{workItem.Title}|{workItem.Priority}|{workItem.DueDate:o}", correlationId, ct);
        if (collaborationService is not null)
        {
            var organizationId = CurrentOrganizationId(workItem.ProjectId);
            await collaborationService.RecordActivityAsync(
                workItem, organizationId, "WorkItemUpdated", "Fields updated", correlationId, ct);
            await collaborationService.NotifyWatchersAsync(
                workItem, organizationId, "WatcherUpdate", $"{workItem.Title} was updated", correlationId, null, ct);
        }
        await PublishRealtimeAsync("updated", workItem, correlationId, ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> SetCustomFieldsAsync(
        string id,
        SetWorkItemCustomFieldsRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, PermissionCatalog.WorkItemUpdate, ct);
        var shape = await typeSchemas.ValidateAsync(workItem.ProjectId, workItem.Type, request.Values, ct);
        var oldValue = string.Join('|', workItem.CustomFields.Select(value => $"{value.FieldKey}:{value.SearchValue}"));
        workItem.Type = shape.IssueTypeKey;
        workItem.IssueTypeSchemaVersion = shape.SchemaVersion;
        workItem.CustomFields = shape.CustomFields.ToList();
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await searchPublisher.IndexAsync(ToScopedSearchRecord(workItem), ct);
        await audit.WriteAsync(
            "WorkItemCustomFieldsUpdated",
            "WorkItem",
            workItem.Id,
            oldValue,
            string.Join('|', workItem.CustomFields.Select(value => $"{value.FieldKey}:{value.SearchValue}")),
            correlationId,
            ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemCustomFieldsUpdated", "Custom fields updated", correlationId, ct);
        await PublishRealtimeAsync("updated", workItem, correlationId, ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> AssignAsync(string id, AssignWorkItemRequest request, string correlationId, CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemAssign", ct);
        if (workItem.TeamId is not null)
        {
            await teamPolicy.EnsureCanAssignAsync(workItem.ProjectId, workItem.TeamId, request.AssigneeUserId, ct);
        }
        var oldAssignee = workItem.AssigneeUserId;
        workItem.AssigneeUserId = request.AssigneeUserId;
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await searchPublisher.IndexAsync(ToScopedSearchRecord(workItem), ct);
        await audit.WriteAsync("WorkItemAssigned", "WorkItem", workItem.Id, oldAssignee, request.AssigneeUserId, correlationId, ct);
        await notifications.NotifyAsync(request.AssigneeUserId, "Assignment", $"Assigned to {workItem.Title}", ct);
        if (collaborationService is not null)
        {
            var organizationId = CurrentOrganizationId(workItem.ProjectId);
            await collaborationService.RecordActivityAsync(
                workItem, organizationId, "WorkItemAssigned", "Assignee changed", correlationId, ct);
            await collaborationService.NotifyWatchersAsync(
                workItem,
                organizationId,
                "WatcherAssignment",
                $"The assignee changed on {workItem.Title}",
                correlationId,
                [request.AssigneeUserId],
                ct);
        }
        await PublishRealtimeAsync("updated", workItem, correlationId, ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> SetTeamAsync(
        string id,
        SetWorkItemTeamRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemUpdate", ct);
        var teamId = NormalizeOptionalId(request.TeamId);
        if (teamId is not null)
        {
            await teamPolicy.EnsureCanAssignAsync(workItem.ProjectId, teamId, workItem.AssigneeUserId, ct);
        }

        if (workItem.TeamId == teamId)
        {
            throw new ConflictException("WORK_ITEM_TEAM_UNCHANGED", "Work item already has the requested team.");
        }

        var oldTeamId = workItem.TeamId;
        workItem.TeamId = teamId;
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await audit.WriteAsync("WorkItemTeamChanged", "WorkItem", workItem.Id, oldTeamId, teamId, correlationId, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemTeamChanged", "Team changed", correlationId, ct);
        await PublishRealtimeAsync("updated", workItem, correlationId, ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> RequestApprovalAsync(
        string id,
        RequestWorkItemApprovalRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemMove", ct);
        await EnsureSeparatedAsync(workItem, ct);
        var target = request.TargetStatus?.Trim();
        if (string.IsNullOrWhiteSpace(target) || workItem.Status.Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Approval target status must differ from the current status.");
        }

        var rule = await workflowPolicy.EnsureTransitionAllowedAsync(workItem.ProjectId, workItem.Type, workItem.Status, target, ct);
        if (!rule.RequiresApproval)
        {
            throw new ConflictException("WORK_ITEM_APPROVAL_NOT_REQUIRED", "The requested transition does not require approval.");
        }

        var now = clock.UtcNow;
        if (workItem.Approvals.Any(x =>
            x.FromStatus.Equals(workItem.Status, StringComparison.OrdinalIgnoreCase)
            && x.ToStatus.Equals(target, StringComparison.OrdinalIgnoreCase)
            && x.ConsumedAt is null
            && x.ExpiresAt > now
            && x.Status is "Pending" or "Approved"))
        {
            throw new ConflictException("WORK_ITEM_APPROVAL_EXISTS", "An active approval already exists for this transition.");
        }

        var approval = new WorkItemApprovalDocument
        {
            FromStatus = workItem.Status,
            ToStatus = rule.ToStatus,
            RequestedByUserId = currentUser.UserId ?? "system",
            RequestedAt = now,
            ExpiresAt = now.AddDays(7)
        };
        workItem.Approvals.Add(approval);
        workItem.UpdatedAt = now;
        await SaveAsync(workItem, ct);
        await activityStore.CreateApprovalAsync(
            WorkItemActivityStore.ToActivity(workItem, CurrentOrganizationId(workItem.ProjectId), approval),
            ct);
        await audit.WriteAsync("WorkItemApprovalRequested", "WorkItem", workItem.Id, null, approval.Id, correlationId, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemApprovalRequested", "Approval requested", correlationId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> DecideApprovalAsync(
        string id,
        string approvalId,
        DecideWorkItemApprovalRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemApprove", ct);
        await EnsureSeparatedAsync(workItem, ct);
        var approval = workItem.Approvals.SingleOrDefault(x => x.Id == approvalId)
            ?? throw new NotFoundException("WORK_ITEM_APPROVAL_NOT_FOUND", "Work item approval was not found.");
        if (approval.Status != "Pending")
        {
            throw new ConflictException("WORK_ITEM_APPROVAL_DECIDED", "Work item approval has already been decided.");
        }

        var now = clock.UtcNow;
        if (approval.ExpiresAt <= now)
        {
            approval.Status = "Expired";
            workItem.UpdatedAt = now;
            await SaveAsync(workItem, ct);
            await UpdateApprovalActivityAsync(workItem, approval, ct);
            await RecordActivityAndNotifyWatchersAsync(
                workItem, "WorkItemApprovalExpired", "Approval expired", correlationId, ct);
            throw new ConflictException("WORK_ITEM_APPROVAL_EXPIRED", "Work item approval has expired.");
        }

        var actorUserId = currentUser.UserId ?? "system";
        if (approval.RequestedByUserId == actorUserId)
        {
            throw new ForbiddenException("Approval requester cannot decide their own request.");
        }

        var note = request.Note?.Trim();
        if (note?.Length > 1000)
        {
            throw new ValidationException("Approval note cannot exceed 1000 characters.");
        }

        approval.Status = request.Approved ? "Approved" : "Rejected";
        approval.DecidedByUserId = actorUserId;
        approval.DecidedAt = now;
        approval.Note = string.IsNullOrWhiteSpace(note) ? null : note;
        workItem.UpdatedAt = now;
        await SaveAsync(workItem, ct);
        await UpdateApprovalActivityAsync(workItem, approval, ct);
        await audit.WriteAsync("WorkItemApprovalDecided", "WorkItem", workItem.Id, "Pending", approval.Status, correlationId, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemApprovalDecided", $"Approval {approval.Status.ToLowerInvariant()}", correlationId, ct);
        await notifications.NotifyAsync(
            approval.RequestedByUserId,
            "Approval",
            $"Approval for {workItem.Title} was {approval.Status.ToLowerInvariant()}.",
            ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> MoveAsync(string id, MoveWorkItemRequest request, string correlationId, CancellationToken ct)
    {
        var initialWorkItem = await GetWorkItem(id, ct);
        await using var structureLock = await AcquireRequiredLockAsync("project-structure:" + initialWorkItem.ProjectId, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemMove", ct);
        await EnsureSeparatedAsync(workItem, ct);
        var target = request.Status.Trim();
        var aggregate = WorkItemAggregate.Rehydrate(workItem);
        aggregate.EnsureCanTarget(target);

        var rule = await workflowPolicy.EnsureTransitionAllowedAsync(workItem.ProjectId, workItem.Type, workItem.Status, target, ct);
        var preparedTransition = aggregate.PrepareTransition(rule, clock.UtcNow);

        var placement = await boardPlacementPolicy.EnsureCanMoveAsync(
            workItem.ProjectId,
            workItem.BoardId,
            workItem.Id,
            target,
            ct);
        var targetRank = await ranks.NextRankAsync(workItem.BoardId, placement.ColumnId, workItem.Id, ct);

        if (rule.ToStatusCategory.Equals("Done", StringComparison.OrdinalIgnoreCase))
        {
            await EnsureCanCompleteAsync(workItem, ct);
        }

        var oldStatus = string.Empty;
        await using (await AcquirePlacementLockAsync(workItem.BoardId, placement, ct))
        {
            if (wipProjection is null)
            {
                await boardPlacementPolicy.EnsureHasCapacityAsync(workItem.BoardId, placement.ColumnId, workItem.Id, ct);
            }
            else
            {
                await wipProjection.ReserveMoveAsync(workItem, placement, ct);
            }
            oldStatus = workItem.Status;
            var now = clock.UtcNow;
            aggregate.Move(
                rule,
                placement,
                targetRank,
                preparedTransition,
                now,
                currentUser.UserId ?? "system");
            await SaveAsync(workItem, ct);
            foreach (var approval in workItem.Approvals)
            {
                await UpdateApprovalActivityAsync(workItem, approval, ct);
            }
            await activityStore.CreateTimelineAsync(
                WorkItemActivityStore.ToActivity(
                    workItem,
                    CurrentOrganizationId(workItem.ProjectId),
                    workItem.StatusHistory[^1],
                    workItem.StatusHistory.Count - 1),
                ct);
        }
        await searchPublisher.IndexAsync(ToScopedSearchRecord(workItem), ct);
        await audit.WriteAsync("WorkItemMoved", "WorkItem", workItem.Id, oldStatus, placement.Status, correlationId, ct);
        if (collaborationService is not null)
        {
            var organizationId = CurrentOrganizationId(workItem.ProjectId);
            await collaborationService.RecordActivityAsync(
                workItem,
                organizationId,
                "WorkItemMoved",
                $"{oldStatus} -> {placement.Status}",
                correlationId,
                ct);
            await collaborationService.NotifyWatchersAsync(
                workItem,
                organizationId,
                "WatcherStatus",
                $"{workItem.Title} moved to {placement.Status}",
                correlationId,
                null,
                ct);
        }
        if (rule.Automations?.Count > 0)
        {
            await audit.WriteAsync(
                "WorkItemAutomationApplied",
                "WorkItem",
                workItem.Id,
                null,
                string.Join(',', rule.Automations.Select(x => x.Action)),
                correlationId,
                ct);
        }
        await PublishRealtimeAsync("moved", workItem, correlationId, ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> ReorderAsync(
        string id,
        ReorderWorkItemRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var initialWorkItem = await GetWorkItem(id, ct);
        await using var structureLock = await AcquireRequiredLockAsync("project-structure:" + initialWorkItem.ProjectId, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemMove", ct);

        var rank = await ranks.ResolveReorderRankAsync(workItem, request, ct);
        var oldRank = workItem.Rank;
        workItem.Rank = rank;
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await audit.WriteAsync(
            "WorkItemReordered",
            "WorkItem",
            workItem.Id,
            oldRank.ToString(System.Globalization.CultureInfo.InvariantCulture),
            rank.ToString(System.Globalization.CultureInfo.InvariantCulture),
            correlationId,
            ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemReordered", "Rank changed", correlationId, ct);
        await PublishRealtimeAsync("reordered", workItem, correlationId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> SetPlanningAsync(string id, SetWorkItemPlanningRequest request, CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemUpdate", ct);
        await (sprintPolicy ?? new NoOpWorkItemSprintPolicy()).EnsurePlanningAllowedAsync(
            workItem.ProjectId,
            workItem.SprintId,
            request.SprintId,
            ct);
        var aggregate = WorkItemAggregate.Rehydrate(workItem);
        aggregate.Plan(request.SprintId, request.EstimatePoints, clock.UtcNow);
        await SaveAsync(workItem, ct);
        await searchPublisher.IndexAsync(ToScopedSearchRecord(workItem), ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem,
            "WorkItemPlanningUpdated",
            "Planning updated",
            MutationEventId(workItem, "planning"),
            ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> AddChecklistItemAsync(string id, AddChecklistItemRequest request, CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemUpdate", ct);
        workItem.Checklist.Add(new ChecklistItemDocument { Text = request.Text.Trim() });
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        var checklistItem = workItem.Checklist[^1];
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemChecklistItemAdded", "Checklist item added", checklistItem.Id, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> CompleteChecklistItemAsync(string id, string itemId, CompleteChecklistItemRequest request, CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemUpdate", ct);
        var item = workItem.Checklist.SingleOrDefault(x => x.Id == itemId)
            ?? throw new NotFoundException("CHECKLIST_ITEM_NOT_FOUND", "Checklist item was not found.");
        item.Completed = request.Completed;
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem,
            "WorkItemChecklistItemUpdated",
            request.Completed ? "Checklist item completed" : "Checklist item reopened",
            MutationEventId(workItem, "checklist:" + itemId),
            ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> AddLabelAsync(string id, AddLabelRequest request, CancellationToken ct)
    {
        var label = request.Label.Trim();
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ValidationException("Label is required.");
        }

        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemUpdate", ct);
        if (workItem.Labels.Any(x => x.Equals(label, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("WORK_ITEM_LABEL_EXISTS", "Work item already has this label.");
        }

        workItem.Labels.Add(label);
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await searchPublisher.IndexAsync(ToScopedSearchRecord(workItem), ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemLabelAdded", "Label added", MutationEventId(workItem, "label:add:" + label), ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> RemoveLabelAsync(string id, string label, CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemUpdate", ct);
        var removed = workItem.Labels.RemoveAll(x => x.Equals(label, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            throw new NotFoundException("WORK_ITEM_LABEL_NOT_FOUND", "Work item label was not found.");
        }

        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await searchPublisher.IndexAsync(ToScopedSearchRecord(workItem), ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemLabelRemoved", "Label removed", MutationEventId(workItem, "label:remove:" + label), ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> AddCommentAsync(string id, AddCommentRequest request, string correlationId, CancellationToken ct)
    {
        var body = NormalizeCommentBody(request.Body);
        var mentions = NormalizeMentions(request.Mentions);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "CommentCreate", ct);
        var organizationId = CurrentOrganizationId(workItem.ProjectId);
        if (collaborationService is not null)
        {
            await collaborationService.ValidateMentionsAsync(
                organizationId,
                workItem.ProjectId,
                mentions,
                ct);
        }
        await EnsureSeparatedAsync(workItem, ct);
        if (workItem.Comments.Count >= 500)
        {
            throw new ConflictException("WORK_ITEM_COMMENT_LIMIT", "A work item cannot contain more than 500 comments.");
        }

        var comment = new CommentDocument
        {
            Body = body,
            AuthorUserId = currentUser.UserId ?? "system",
            Mentions = mentions,
            CreatedAt = clock.UtcNow
        };

        await activityStore.CreateCommentAsync(
            WorkItemActivityStore.ToActivity(workItem, CurrentOrganizationId(workItem.ProjectId), comment),
            ct);
        workItem.Comments.Add(comment);
        await audit.WriteAsync("WorkItemCommentAdded", "WorkItem", workItem.Id, null, comment.Id, correlationId, ct);

        foreach (var mentionedUserId in comment.Mentions)
        {
            if (mentionedUserId != currentUser.UserId)
            {
                await notifications.NotifyAsync(
                    mentionedUserId,
                    "Mention",
                    $"Mentioned on {workItem.Title}",
                    ct,
                    $"mention:{workItem.Id}:{comment.Id}:{mentionedUserId}");
            }
        }
        if (collaborationService is not null)
        {
            await collaborationService.RecordActivityAsync(
                workItem,
                organizationId,
                "WorkItemCommentAdded",
                "Comment added",
                comment.Id,
                ct);
            await collaborationService.NotifyWatchersAsync(
                workItem,
                organizationId,
                "WatcherComment",
                $"A comment was added to {workItem.Title}",
                comment.Id,
                comment.Mentions,
                ct);
        }

        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> EditCommentAsync(string id, string commentId, EditCommentRequest request, string correlationId, CancellationToken ct)
    {
        var body = NormalizeCommentBody(request.Body);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "CommentCreate", ct);
        await EnsureSeparatedAsync(workItem, ct);
        var comment = workItem.Comments.SingleOrDefault(x => x.Id == commentId)
            ?? throw new NotFoundException("COMMENT_NOT_FOUND", "Comment was not found.");

        if (!string.Equals(comment.AuthorUserId, currentUser.UserId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Only the comment author can edit this comment.");
        }

        if (comment.Body == body)
        {
            throw new ConflictException("COMMENT_UNCHANGED", "Comment body is unchanged.");
        }

        if (comment.History.Count >= 100)
        {
            throw new ConflictException("COMMENT_HISTORY_LIMIT", "A comment cannot contain more than 100 revisions.");
        }

        var oldValue = comment.Body;
        var now = clock.UtcNow;
        comment.History.Add(new CommentRevisionDocument
        {
            Body = oldValue,
            EditedByUserId = currentUser.UserId ?? "system",
            EditedAt = now
        });
        comment.Body = body;
        comment.EditedAt = now;
        var organizationId = CurrentOrganizationId(workItem.ProjectId);
        var storedComment = await activityStore.GetCommentAsync(
            organizationId, workItem.ProjectId, workItem.Id, comment.Id, ct)
            ?? throw new NotFoundException("COMMENT_NOT_FOUND", "Comment was not found.");
        var revision = WorkItemActivityStore.ToActivity(
            workItem,
            organizationId,
            comment.Id,
            comment.History[^1],
            comment.History.Count - 1);
        await activityStore.CreateRevisionAsync(revision, ct);
        storedComment.Body = comment.Body;
        storedComment.EditedAt = comment.EditedAt;
        await activityStore.UpdateCommentAsync(storedComment, ct);
        await audit.WriteAsync("WorkItemCommentEdited", "WorkItem", workItem.Id, oldValue, comment.Id, correlationId, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem,
            "WorkItemCommentEdited",
            "Comment edited",
            $"comment:{commentId}:revision:{comment.History.Count}",
            ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> DeleteCommentAsync(string id, string commentId, string correlationId, CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "CommentCreate", ct);
        await EnsureSeparatedAsync(workItem, ct);
        var comment = workItem.Comments.SingleOrDefault(x => x.Id == commentId)
            ?? throw new NotFoundException("COMMENT_NOT_FOUND", "Comment was not found.");

        if (!string.Equals(comment.AuthorUserId, currentUser.UserId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Only the comment author can delete this comment.");
        }

        var storedComment = await activityStore.GetCommentAsync(
            CurrentOrganizationId(workItem.ProjectId), workItem.ProjectId, workItem.Id, comment.Id, ct)
            ?? throw new NotFoundException("COMMENT_NOT_FOUND", "Comment was not found.");
        await activityStore.DeleteCommentAsync(storedComment, ct);
        workItem.Comments.Remove(comment);
        await audit.WriteAsync("WorkItemCommentDeleted", "WorkItem", workItem.Id, comment.Body, null, correlationId, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemCommentDeleted", "Comment deleted", correlationId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> UploadAttachmentAsync(
        string id,
        Stream content,
        string fileName,
        string contentType,
        long declaredSizeBytes,
        string correlationId,
        CancellationToken ct)
    {
        const long maxSizeBytes = 25 * 1024 * 1024;
        if (declaredSizeBytes is <= 0 or > maxSizeBytes)
        {
            throw new ValidationException("Attachment size must be between 1 byte and 25 MB.");
        }

        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 180)
        {
            throw new ValidationException("Attachment file name is required and cannot exceed 180 characters.");
        }

        await using var workItemLock = await AcquireRequiredLockAsync("work-item:" + id, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "AttachmentCreate", ct);
        await EnsureSeparatedAsync(workItem, ct);
        if (workItem.Attachments.Count >= 100)
        {
            throw new ConflictException("ATTACHMENT_LIMIT_REACHED", "A work item cannot contain more than 100 attachments.");
        }

        var stored = await attachmentStorage.SaveAsync(content, fileName, contentType, maxSizeBytes, ct);
        var attachment = new AttachmentDocument
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
            CreatedAt = clock.UtcNow
        };
        workItem.Attachments.Add(attachment);
        try
        {
            await activityStore.CreateAttachmentAsync(
                WorkItemActivityStore.ToActivity(workItem, CurrentOrganizationId(workItem.ProjectId), attachment),
                ct);
        }
        catch
        {
            await attachmentStorage.DeleteAsync(stored.StoragePath, CancellationToken.None);
            throw;
        }

        await audit.WriteAsync(
            "WorkItemAttachmentUploaded",
            "WorkItem",
            workItem.Id,
            null,
            $"{attachment.Id}:{attachment.FileName}:{attachment.SizeBytes}",
            correlationId,
            ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemAttachmentUploaded", "Attachment uploaded", attachment.Id, ct);
        return ToResponse(workItem);
    }

    public async Task<AttachmentFile> OpenAttachmentAsync(string id, string attachmentId, CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemView", ct);
        if (workItem.ActivityStorageVersion < 1)
        {
            var legacy = workItem.Attachments.SingleOrDefault(x => x.Id == attachmentId)
                ?? throw new NotFoundException("ATTACHMENT_NOT_FOUND", "Attachment was not found.");
            EnsureAttachmentIsClean(legacy.SecurityState);
            var legacyContent = await attachmentStorage.OpenReadAsync(
                legacy.StoragePath, legacy.ContentType, legacy.ChecksumSha256, ct);
            return new AttachmentFile(legacyContent, legacy.FileName, legacy.ContentType, legacy.SizeBytes);
        }

        var attachment = await activityStore.GetAttachmentAsync(
            CurrentOrganizationId(workItem.ProjectId), workItem.ProjectId, workItem.Id, attachmentId, ct)
            ?? throw new NotFoundException("ATTACHMENT_NOT_FOUND", "Attachment was not found.");
        EnsureAttachmentIsClean(attachment.SecurityState);
        var content = await attachmentStorage.OpenReadAsync(
            attachment.StoragePath, attachment.ContentType, attachment.ChecksumSha256, ct);
        return new AttachmentFile(content, attachment.FileName, attachment.ContentType, attachment.SizeBytes);
    }

    public async Task<WorkItemResponse> DeleteAttachmentAsync(
        string id,
        string attachmentId,
        string correlationId,
        CancellationToken ct)
    {
        await using var workItemLock = await AcquireRequiredLockAsync("work-item:" + id, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "AttachmentDelete", ct);
        await EnsureSeparatedAsync(workItem, ct);
        var attachment = await activityStore.GetAttachmentAsync(
            CurrentOrganizationId(workItem.ProjectId), workItem.ProjectId, workItem.Id, attachmentId, ct)
            ?? throw new NotFoundException("ATTACHMENT_NOT_FOUND", "Attachment was not found.");
        await activityStore.DeleteAttachmentAsync(attachment, ct);
        workItem.Attachments.RemoveAll(x => x.Id == attachment.Id);
        try
        {
            await attachmentStorage.DeleteAsync(attachment.StoragePath, ct);
        }
        catch
        {
            await activityStore.CreateAttachmentAsync(attachment, CancellationToken.None);
            workItem.Attachments.Add(new AttachmentDocument
            {
                Id = attachment.Id,
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                SizeBytes = attachment.SizeBytes,
                StoragePath = attachment.StoragePath,
                ChecksumSha256 = attachment.ChecksumSha256,
                SecurityState = attachment.SecurityState,
                ScanProvider = attachment.ScanProvider,
                ScanDetail = attachment.ScanDetail,
                ScannedAt = attachment.ScannedAt,
                CreatedAt = attachment.CreatedAt
            });
            throw;
        }

        await audit.WriteAsync(
            "WorkItemAttachmentDeleted",
            "WorkItem",
            workItem.Id,
            $"{attachment.Id}:{attachment.FileName}:{attachment.SizeBytes}",
            null,
            correlationId,
            ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemAttachmentDeleted", "Attachment deleted", correlationId, ct);
        return ToResponse(workItem);
    }

    private static void EnsureAttachmentIsClean(string securityState)
    {
        if (!securityState.Equals(AttachmentSecurityStates.Clean, StringComparison.Ordinal))
        {
            throw new ConflictException(
                "ATTACHMENT_NOT_CLEAN",
                "Attachment content is not available until security scanning completes successfully.");
        }
    }

    public async Task<WorkItemResponse> AddWorkLogAsync(string id, AddWorkLogRequest request, CancellationToken ct)
    {
        if (request.Hours <= 0 || request.Hours > 24)
        {
            throw new ValidationException("Work log hours must be between 0 and 24.");
        }

        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkLogCreate", ct);
        await EnsureSeparatedAsync(workItem, ct);
        var workLog = new WorkLogDocument
        {
            UserId = request.UserId,
            Hours = request.Hours,
            Note = request.Note,
            CreatedAt = clock.UtcNow
        };
        await activityStore.CreateWorkLogAsync(
            WorkItemActivityStore.ToActivity(workItem, CurrentOrganizationId(workItem.ProjectId), workLog),
            ct);
        workItem.WorkLogs.Add(workLog);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemWorkLogAdded", "Work log added", workLog.Id, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> SetParentAsync(
        string id,
        SetWorkItemParentRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var initialWorkItem = await GetWorkItem(id, ct);
        await using var structureLock = await AcquireRequiredLockAsync("project-structure:" + initialWorkItem.ProjectId, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemUpdate", ct);
        var parent = await ValidateParentAsync(
            workItem.ProjectId,
            workItem.BoardId,
            workItem.Type,
            request.ParentId,
            workItem.Id,
            ct);
        var oldParentId = workItem.ParentId;

        if (string.Equals(oldParentId, parent?.Id, StringComparison.Ordinal))
        {
            throw new ConflictException("WORK_ITEM_PARENT_UNCHANGED", "Work item already has the requested parent.");
        }

        workItem.ParentId = parent?.Id;
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await audit.WriteAsync("WorkItemParentChanged", "WorkItem", workItem.Id, oldParentId, parent?.Id, correlationId, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemParentChanged", "Parent changed", correlationId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> LinkAsync(
        string id,
        LinkWorkItemRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var initialWorkItem = await GetWorkItem(id, ct);
        await using var structureLock = await AcquireRequiredLockAsync("project-structure:" + initialWorkItem.ProjectId, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemLink", ct);
        var relationType = NormalizeRelationType(request.RelationType);

        if (string.Equals(workItem.Id, request.RelatedWorkItemId, StringComparison.Ordinal))
        {
            throw new ValidationException("A work item cannot be linked to itself.");
        }

        var relatedWorkItem = await GetWorkItem(request.RelatedWorkItemId, ct);
        if (!string.Equals(workItem.ProjectId, relatedWorkItem.ProjectId, StringComparison.Ordinal))
        {
            throw new ValidationException("Linked work items must belong to the same project.");
        }

        if (workItem.Relations.Any(x =>
            x.RelatedWorkItemId == relatedWorkItem.Id
            && x.RelationType.Equals(relationType, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("WORK_ITEM_RELATION_EXISTS", "Work item relation already exists.");
        }

        await graph.AddRelationAsync(
            workItem.ProjectId,
            workItem.Id,
            relatedWorkItem.Id,
            relationType,
            ct);

        workItem.Relations.Add(new WorkItemRelationDocument
        {
            RelatedWorkItemId = relatedWorkItem.Id,
            RelationType = relationType
        });
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await audit.WriteAsync("WorkItemLinked", "WorkItem", workItem.Id, null, $"{relationType}:{relatedWorkItem.Id}", correlationId, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemLinked", "Relation added", correlationId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> UnlinkAsync(
        string id,
        string relatedWorkItemId,
        string relationType,
        string correlationId,
        CancellationToken ct)
    {
        var initialWorkItem = await GetWorkItem(id, ct);
        await using var structureLock = await AcquireRequiredLockAsync("project-structure:" + initialWorkItem.ProjectId, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemLink", ct);
        var normalizedType = NormalizeRelationType(relationType);
        var removed = workItem.Relations.RemoveAll(x =>
            x.RelatedWorkItemId == relatedWorkItemId
            && x.RelationType.Equals(normalizedType, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            throw new NotFoundException("WORK_ITEM_RELATION_NOT_FOUND", "Work item relation was not found.");
        }

        await graph.RemoveRelationAsync(
            workItem.ProjectId,
            workItem.Id,
            relatedWorkItemId,
            normalizedType,
            ct);
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await audit.WriteAsync("WorkItemUnlinked", "WorkItem", workItem.Id, $"{normalizedType}:{relatedWorkItemId}", null, correlationId, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemUnlinked", "Relation removed", correlationId, ct);
        return ToResponse(workItem);
    }

    public async Task ArchiveAsync(string id, string correlationId, CancellationToken ct)
    {
        var initialWorkItem = await GetWorkItem(id, ct);
        await using var structureLock = await AcquireRequiredLockAsync("project-structure:" + initialWorkItem.ProjectId, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemDelete", ct);
        await EnsureHasNoActiveChildrenAsync(workItem.Id, ct);
        if (wipProjection is not null)
        {
            await wipProjection.ReleaseAsync(workItem, ct);
        }
        workItem.Archived = true;
        workItem.UpdatedAt = clock.UtcNow;
        await SaveAsync(workItem, ct);
        await searchPublisher.DeleteAsync(workItem.Id, ct);
        await audit.WriteAsync("WorkItemArchived", "WorkItem", workItem.Id, "active", "archived", correlationId, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemArchived", "Work item archived", correlationId, ct);
        await PublishRealtimeAsync("archived", workItem, correlationId, ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
    }

    public async Task<WorkItemResponse> RestoreAsync(string id, string correlationId, CancellationToken ct)
    {
        var initialWorkItem = await GetArchivedWorkItem(id, ct);
        await using var structureLock = await AcquireRequiredLockAsync("project-structure:" + initialWorkItem.ProjectId, ct);
        var workItem = await GetArchivedWorkItem(id, ct);
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
                await boardPlacementPolicy.EnsureHasCapacityAsync(workItem.BoardId, placement.ColumnId, workItem.Id, ct);
            }
            else
            {
                await wipProjection.ReserveCreateAsync(workItem.ProjectId, workItem.BoardId, placement, ct);
            }
            workItem.ColumnId = placement.ColumnId;
            workItem.Status = placement.Status;
            workItem.Rank = await ranks.NextRankAsync(workItem.BoardId, placement.ColumnId, workItem.Id, ct);
            workItem.Archived = false;
            workItem.UpdatedAt = clock.UtcNow;
            await SaveAsync(workItem, ct);
        }

        await searchPublisher.IndexAsync(ToScopedSearchRecord(workItem), ct);
        await audit.WriteAsync("WorkItemRestored", "WorkItem", workItem.Id, "archived", "active", correlationId, ct);
        await RecordActivityAndNotifyWatchersAsync(
            workItem, "WorkItemRestored", "Work item restored", correlationId, ct);
        await PublishRealtimeAsync("restored", workItem, correlationId, ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(workItem.ProjectId, ct);
        return ToResponse(workItem);
    }

    public Task<BulkWorkItemResponse> BulkMoveAsync(BulkMoveWorkItemsRequest request, string correlationId, CancellationToken ct) =>
        ExecuteBulkAsync(
            request.WorkItemIds,
            (id, itemCorrelationId, token) => MoveAsync(id, new MoveWorkItemRequest(request.Status), itemCorrelationId, token),
            correlationId,
            ct);

    public Task<BulkWorkItemResponse> BulkAssignAsync(BulkAssignWorkItemsRequest request, string correlationId, CancellationToken ct) =>
        ExecuteBulkAsync(
            request.WorkItemIds,
            (id, itemCorrelationId, token) => AssignAsync(id, new AssignWorkItemRequest(request.AssigneeUserId), itemCorrelationId, token),
            correlationId,
            ct);

    public Task<BulkWorkItemResponse> BulkArchiveAsync(BulkArchiveWorkItemsRequest request, string correlationId, CancellationToken ct) =>
        ExecuteBulkAsync(
            request.WorkItemIds,
            async (id, itemCorrelationId, token) =>
            {
                await ArchiveAsync(id, itemCorrelationId, token);
                return true;
            },
            correlationId,
            ct);

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

    private static string MutationEventId(WorkItemDocument workItem, string discriminator) =>
        $"{discriminator}:{workItem.UpdatedAt.ToUniversalTime().Ticks}";

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
                ToRealtimeItem(workItem),
                correlationId,
                clock.UtcNow,
                WorkItemRealtimeProtocol.CurrentSchemaVersion,
                workItem.Version),
            ct);

    public async Task<int> SendDueDateRemindersAsync(int horizonHours, CancellationToken ct)
    {
        await using var dispatcherLock = await AcquireRequiredLockAsync("due-date-reminder-dispatcher", ct);
        var now = clock.UtcNow;
        var until = now.AddHours(Math.Clamp(horizonHours, 1, 168));
        var candidates = await workItems.ListByFilterAsync(
            x => !x.Archived
                && x.CompletedAt == null
                && x.AssigneeUserId != null
                && x.DueDate != null
                && x.DueDate > now
                && x.DueDate <= until
                && x.DueReminderSentAt == null,
            x => x.DueDate!,
            pageSize: 500,
            cancellationToken: ct);
        var sent = 0;
        foreach (var candidate in candidates)
        {
            await using var workItemLock = await AcquireRequiredLockAsync("work-item:" + candidate.Id, ct);
            var workItem = await workItems.SelectAsync(x => x.Id == candidate.Id && !x.Archived, ct);
            if (workItem?.AssigneeUserId is null)
            {
                continue;
            }

            try
            {
                var authorization = await permissionChecker.EnsureCanAsync(
                    workItem.AssigneeUserId,
                    workItem.ProjectId,
                    PermissionCatalog.WorkItemView,
                    ct);
                authorizedOrganizationIds[workItem.ProjectId] = authorization.OrganizationId;
            }
            catch (Exception exception) when (exception is ForbiddenException or NotFoundException)
            {
                continue;
            }

            if (workItem.CompletedAt is not null
                || workItem.DueDate is null
                || workItem.DueDate <= now
                || workItem.DueDate > until
                || workItem.DueReminderSentAt is not null)
            {
                continue;
            }

            var deduplicationKey = $"due:{workItem.Id}:{workItem.DueDate.Value.UtcTicks}";
            await notifications.NotifyAsync(
                workItem.AssigneeUserId,
                "DueDateReminder",
                $"{workItem.Title} is due at {workItem.DueDate:O}.",
                ct,
                deduplicationKey);
            workItem.DueReminderSentAt = clock.UtcNow;
            workItem.UpdatedAt = clock.UtcNow;
            await SaveAsync(workItem, ct);
            sent++;
        }

        return sent;
    }

    private static double? TryCalculateCycleTimeHours(
        WorkItemDocument item,
        IReadOnlyCollection<WorkItemStatusHistoryResponse> statusHistory)
    {
        if (item.CompletedAt is null)
        {
            return null;
        }

        var history = statusHistory.OrderBy(x => x.ChangedAt).ToList();
        var previousCompletion = history
            .Where(x => x.ChangedAt < item.CompletedAt && IsCompletedStatus(x.ToStatus))
            .Select(x => (DateTimeOffset?)x.ChangedAt)
            .LastOrDefault();
        var startedAt = history
            .Where(x => x.ChangedAt <= item.CompletedAt
                && (!previousCompletion.HasValue || x.ChangedAt > previousCompletion.Value)
                && IsActiveStatus(x.ToStatus))
            .Select(x => (DateTimeOffset?)x.ChangedAt)
            .FirstOrDefault();

        return startedAt.HasValue
            ? Math.Max(0, (item.CompletedAt.Value - startedAt.Value).TotalHours)
            : null;
    }

    private static bool IsCompletedStatus(string status) =>
        status.Equals("Done", StringComparison.OrdinalIgnoreCase)
        || status.Equals("Closed", StringComparison.OrdinalIgnoreCase);

    private static bool IsActiveStatus(string status) =>
        !status.Equals("Backlog", StringComparison.OrdinalIgnoreCase)
        && !status.Equals("To Do", StringComparison.OrdinalIgnoreCase)
        && !status.Equals("Open", StringComparison.OrdinalIgnoreCase)
        && !IsCompletedStatus(status);

    private static double? Average(IReadOnlyCollection<double> values) =>
        values.Count == 0 ? null : Math.Round(values.Average(), 2);

    private static double? Median(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var ordered = values.OrderBy(x => x).ToArray();
        var middle = ordered.Length / 2;
        var value = ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2
            : ordered[middle];
        return Math.Round(value, 2);
    }

    private (DateOnly From, DateOnly To, DateTimeOffset FromInstant, DateTimeOffset ToInstant) NormalizeReportRange(
        DateOnly? from,
        DateOnly? to)
    {
        var reportTo = to ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var reportFrom = from ?? reportTo.AddDays(-29);
        if (reportTo < reportFrom)
        {
            throw new ValidationException("Report end date must be after start date.");
        }

        if (reportTo.DayNumber - reportFrom.DayNumber + 1 > 366)
        {
            throw new ValidationException("Report range cannot exceed 366 days.");
        }

        return (
            reportFrom,
            reportTo,
            new DateTimeOffset(reportFrom.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(reportTo.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero));
    }

    private async Task<WorkItemDocument?> ValidateParentAsync(
        string projectId,
        string boardId,
        string type,
        string? parentId,
        string? workItemId,
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

        if (string.Equals(parentId, workItemId, StringComparison.Ordinal))
        {
            throw new ValidationException("A work item cannot be its own parent.");
        }

        var parent = await GetWorkItem(parentId, ct);
        if (!string.Equals(parent.ProjectId, projectId, StringComparison.Ordinal))
        {
            throw new ValidationException("A parent work item must belong to the same project.");
        }

        if (parent.CompletedAt is not null || IsCompletedStatus(parent.Status))
        {
            throw new ConflictException("WORK_ITEM_PARENT_COMPLETED", "A completed work item cannot receive a child.");
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

        await graph.EnsureCanSetParentAsync(projectId, workItemId, parent.Id, ct);

        return parent;
    }

    private async Task EnsureCanCompleteAsync(WorkItemDocument workItem, CancellationToken ct)
    {
        await EnsureHasNoActiveChildrenAsync(workItem.Id, ct);
        var blockers = await graph.ActiveBlockerIdsAsync(workItem.ProjectId, workItem.Id, ct);

        if (blockers.Count > 0)
        {
            throw new ConflictException(
                "WORK_ITEM_BLOCKED",
                $"Work item cannot be completed while blockers remain active: {string.Join(", ", blockers)}.");
        }
    }

    private async Task EnsureHasNoActiveChildrenAsync(string workItemId, CancellationToken ct)
    {
        var activeChild = await workItems.SelectAsync(
            x => x.ParentId == workItemId && !x.Archived && x.CompletedAt == null && x.Status != "Done" && x.Status != "Closed",
            ct);
        if (activeChild is not null)
        {
            throw new ConflictException(
                "WORK_ITEM_HAS_ACTIVE_CHILDREN",
                "Work item cannot be completed or archived while it has active children.");
        }
    }

    private async Task<IReadOnlyList<WorkItemDocument>> LoadReportItemsAsync(
        Expression<Func<WorkItemDocument, bool>> filter,
        CancellationToken ct)
    {
        const int pageSize = 200;
        var result = new List<WorkItemDocument>();
        string? cursor = null;
        do
        {
            var page = await workItems.ListByCursorAsync(
                filter,
                cursor,
                pageSize,
                ct);
            result.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return result;
    }

    private Task<WorkItemReportActivityData> ReadReportActivitiesAsync(
        string projectId,
        CancellationToken ct) =>
        activityStore.ReadReportDataAsync(CurrentOrganizationId(projectId), projectId, ct);

    private static decimal LoggedHours(WorkItemDocument item, WorkItemReportActivityData activities) =>
        item.ActivityStorageVersion >= 1
            ? activities.LoggedHoursByWorkItem.GetValueOrDefault(item.Id)
            : item.WorkLogs.Sum(log => log.Hours);

    private static IReadOnlyList<WorkItemStatusHistoryResponse> Timeline(
        WorkItemDocument item,
        WorkItemReportActivityData activities) =>
        item.ActivityStorageVersion >= 1
            ? activities.TimelineByWorkItem.GetValueOrDefault(item.Id) ?? []
            : item.StatusHistory
                .Select(entry => new WorkItemStatusHistoryResponse(
                    entry.FromStatus,
                    entry.ToStatus,
                    entry.ChangedByUserId,
                    entry.ChangedAt))
                .ToList();

    private async Task<BulkWorkItemResponse> ExecuteBulkAsync<T>(
        IReadOnlyCollection<string> workItemIds,
        Func<string, string, CancellationToken, Task<T>> operation,
        string correlationId,
        CancellationToken ct)
    {
        if (workItemIds is null || workItemIds.Count is < 1 or > 100)
        {
            throw new ValidationException("Bulk work item operations require between 1 and 100 ids.");
        }

        var ids = workItemIds.Select(id => id?.Trim() ?? string.Empty).ToList();
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
        {
            throw new ValidationException("Bulk work item ids must be non-empty and unique.");
        }

        var results = new List<BulkWorkItemResult>(ids.Count);
        for (var index = 0; index < ids.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var id = ids[index];
            try
            {
                await operation(id, $"{correlationId}:{index + 1}", ct);
                results.Add(new BulkWorkItemResult(id, true, null, null));
            }
            catch (ZumboException exception)
            {
                results.Add(new BulkWorkItemResult(id, false, exception.Code, exception.Message));
            }
        }

        var succeeded = results.Count(result => result.Success);
        return new BulkWorkItemResponse(results, succeeded, results.Count - succeeded);
    }

    private TimeSpan ReadModelTtl =>
        TimeSpan.FromSeconds(Math.Clamp(readModelCacheOptions.Value.TtlSeconds, 5, 300));

    private static string NormalizeRelationType(string? relationType)
    {
        var requested = string.IsNullOrWhiteSpace(relationType) ? "RelatesTo" : relationType.Trim();
        return requested.ToLowerInvariant() switch
        {
            "blocks" => "Blocks",
            "blockedby" or "blocked-by" => "BlockedBy",
            "relatesto" or "relates-to" => "RelatesTo",
            "duplicates" => "Duplicates",
            _ => throw new ValidationException("Relation type must be Blocks, BlockedBy, RelatesTo or Duplicates.")
        };
    }

    private static string? NormalizeOptionalId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeCommentBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ValidationException("Comment body is required.");
        }

        var normalized = body.Trim();
        if (normalized.Length > 10_000)
        {
            throw new ValidationException("Comment body cannot exceed 10000 characters.");
        }

        return normalized;
    }

    private static List<string> NormalizeMentions(IReadOnlyCollection<string>? mentions)
    {
        var normalized = mentions?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];
        if (normalized.Count > 50)
        {
            throw new ValidationException("A comment cannot mention more than 50 users.");
        }

        if (normalized.Any(x => x.Length > 128))
        {
            throw new ValidationException("Mentioned user ids cannot exceed 128 characters.");
        }

        return normalized;
    }

    private async Task<WorkItemDocument> GetWorkItem(string id, CancellationToken ct)
    {
        var workItem = await workItems.SelectAsync(x => x.Id == id && !x.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        var authorization = await EnsurePermissionAsync(workItem.ProjectId, PermissionCatalog.WorkItemView, ct);
        await activityStore.HydrateAsync(workItem, authorization.OrganizationId, ct);
        return workItem;
    }

    private async Task<WorkItemDocument> GetArchivedWorkItem(string id, CancellationToken ct)
    {
        var workItem = await workItems.SelectAsync(x => x.Id == id && x.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Archived work item was not found.");
        var authorization = await EnsurePermissionAsync(workItem.ProjectId, PermissionCatalog.WorkItemView, ct);
        await activityStore.HydrateAsync(workItem, authorization.OrganizationId, ct);
        return workItem;
    }

    private async Task SaveAsync(WorkItemDocument workItem, CancellationToken ct)
    {
        await activityStore.MigrateEmbeddedAsync(workItem, CurrentOrganizationId(workItem.ProjectId), ct);
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

    private async Task EnsureSeparatedAsync(WorkItemDocument workItem, CancellationToken ct)
    {
        if (workItem.ActivityStorageVersion >= 1)
        {
            return;
        }

        await activityStore.MigrateEmbeddedAsync(workItem, CurrentOrganizationId(workItem.ProjectId), ct);
        await SaveAsync(workItem, ct);
    }

    private async Task HydrateAllAsync(IEnumerable<WorkItemDocument> source, CancellationToken ct)
    {
        foreach (var workItem in source)
        {
            await activityStore.HydrateAsync(workItem, CurrentOrganizationId(workItem.ProjectId), ct);
        }
    }

    private async Task UpdateApprovalActivityAsync(
        WorkItemDocument workItem,
        WorkItemApprovalDocument approval,
        CancellationToken ct)
    {
        var organizationId = CurrentOrganizationId(workItem.ProjectId);
        var stored = WorkItemActivityStore.ToActivity(workItem, organizationId, approval);
        var current = await activityStore.GetApprovalAsync(
            organizationId, workItem.ProjectId, workItem.Id, approval.Id, ct);
        stored.Version = current?.Version
            ?? throw new NotFoundException("WORK_ITEM_APPROVAL_NOT_FOUND", "Work item approval was not found.");
        await activityStore.UpdateApprovalAsync(stored, ct);
    }

    private string CurrentOrganizationId(string projectId)
    {
        if (!authorizedOrganizationIds.TryGetValue(projectId, out var organizationId))
        {
            throw new InvalidOperationException("Project resource must be authorized before tenant data is accessed.");
        }

        return organizationId;
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
            ?? throw new ConflictException("RESOURCE_BUSY", "The requested resource is busy; retry the operation.");
    }

    private static WorkItemResponse ToResponse(WorkItemDocument item) =>
        new(
            item.Id,
            item.ProjectId,
            item.BoardId,
            item.ParentId,
            item.TeamId,
            item.ColumnId,
            item.Title,
            item.Description,
            item.Type,
            item.Priority,
            item.Status,
            item.AssigneeUserId,
            item.DueDate,
            item.SprintId,
            item.EstimatePoints,
            item.CompletedAt,
            item.StatusHistory
                .OrderBy(x => x.ChangedAt)
                .Select(x => new WorkItemStatusHistoryResponse(x.FromStatus, x.ToStatus, x.ChangedByUserId, x.ChangedAt))
                .ToList(),
            item.Labels,
            item.Checklist.Select(x => new ChecklistItemResponse(x.Id, x.Text, x.Completed)).ToList(),
            item.Comments.Select(x => new CommentResponse(
                x.Id,
                x.Body,
                x.AuthorUserId,
                x.Mentions,
                x.CreatedAt,
                x.EditedAt,
                x.History
                    .OrderBy(revision => revision.EditedAt)
                    .Select(revision => new CommentRevisionResponse(
                        revision.Body,
                        revision.EditedByUserId,
                        revision.EditedAt))
                    .ToList())).ToList(),
            item.Attachments.Select(x => new AttachmentResponse(
                x.Id, x.FileName, x.ContentType, x.SizeBytes, x.CreatedAt,
                x.SecurityState, x.ScanProvider, x.ScannedAt)).ToList(),
            item.WorkLogs.Select(x => new WorkLogResponse(x.Id, x.UserId, x.Hours, x.Note, x.CreatedAt)).ToList(),
            item.Relations.Select(x => new WorkItemRelationResponse(x.RelatedWorkItemId, x.RelationType)).ToList(),
            item.Approvals
                .OrderBy(x => x.RequestedAt)
                .Select(x => new WorkItemApprovalResponse(
                    x.Id,
                    x.FromStatus,
                    x.ToStatus,
                    x.RequestedByUserId,
                    x.RequestedAt,
                    x.ExpiresAt,
                    x.Status,
                    x.DecidedByUserId,
                    x.DecidedAt,
                    x.Note,
                    x.ConsumedAt))
                .ToList(),
            item.Rank,
            item.Archived,
            item.Version,
            item.IssueTypeSchemaVersion,
            item.CustomFields.Select(value => new WorkItemCustomFieldValueResponse(
                value.FieldKey,
                value.Type,
                value.TextValue,
                value.NumberValue,
                value.BooleanValue,
                value.DateValueUtc is null ? null : DateOnly.FromDateTime(value.DateValueUtc.Value.UtcDateTime),
                value.OptionKey)).ToList());

    internal static WorkItemRealtimeItem ToRealtimeItem(WorkItemDocument item) =>
        new(
            item.Id,
            item.ProjectId,
            item.BoardId,
            item.ColumnId,
            item.Title,
            item.Type,
            item.Priority,
            item.Status,
            item.AssigneeUserId,
            item.DueDate,
            item.SprintId,
            item.EstimatePoints,
            item.CompletedAt,
            item.Rank,
            item.Version);
}
