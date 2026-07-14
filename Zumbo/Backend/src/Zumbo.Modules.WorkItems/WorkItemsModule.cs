using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Infrastructure.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.BuildingBlocks.Infrastructure.Search;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Notifications;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record CreateWorkItemRequest(
    string ProjectId,
    string BoardId,
    string Title,
    string Type,
    string Priority,
    string? AssigneeUserId,
    DateTimeOffset? DueDate,
    string? ParentId = null,
    string? TeamId = null);

public sealed record UpdateWorkItemRequest(string? Title, string? Description, string? Priority, DateTimeOffset? DueDate);
public sealed record AssignWorkItemRequest(string AssigneeUserId);
public sealed record MoveWorkItemRequest(string Status);
public sealed record ReorderWorkItemRequest(string? BeforeWorkItemId, string? AfterWorkItemId);
public sealed record SetWorkItemPlanningRequest(string? SprintId, decimal? EstimatePoints);
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
public sealed record WorkItemSearchRequest(
    string? ProjectId,
    string? AssigneeUserId,
    string? Status,
    string? Text,
    int Page = 1,
    int PageSize = 100,
    bool Archived = false);
public sealed record BulkMoveWorkItemsRequest(IReadOnlyCollection<string> WorkItemIds, string Status);
public sealed record BulkAssignWorkItemsRequest(IReadOnlyCollection<string> WorkItemIds, string AssigneeUserId);
public sealed record BulkArchiveWorkItemsRequest(IReadOnlyCollection<string> WorkItemIds);
public sealed record BulkWorkItemResult(string WorkItemId, bool Success, string? ErrorCode, string? ErrorMessage);
public sealed record BulkWorkItemResponse(IReadOnlyCollection<BulkWorkItemResult> Results, int Succeeded, int Failed);

public sealed record WorkItemResponse(
    string Id,
    string ProjectId,
    string BoardId,
    string? ParentId,
    string? TeamId,
    string ColumnId,
    string Title,
    string Description,
    string Type,
    string Priority,
    string Status,
    string? AssigneeUserId,
    DateTimeOffset? DueDate,
    string? SprintId,
    decimal EstimatePoints,
    DateTimeOffset? CompletedAt,
    IReadOnlyCollection<WorkItemStatusHistoryResponse> StatusHistory,
    IReadOnlyCollection<string> Labels,
    IReadOnlyCollection<ChecklistItemResponse> Checklist,
    IReadOnlyCollection<CommentResponse> Comments,
    IReadOnlyCollection<AttachmentResponse> Attachments,
    IReadOnlyCollection<WorkLogResponse> WorkLogs,
    IReadOnlyCollection<WorkItemRelationResponse> Relations,
    IReadOnlyCollection<WorkItemApprovalResponse> Approvals,
    long Rank = 0,
    bool Archived = false);

public sealed record ChecklistItemResponse(string Id, string Text, bool Completed);
public sealed record CommentResponse(
    string Id,
    string Body,
    string AuthorUserId,
    IReadOnlyCollection<string> Mentions,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    IReadOnlyCollection<CommentRevisionResponse> History);
public sealed record CommentRevisionResponse(string Body, string EditedByUserId, DateTimeOffset EditedAt);
public sealed record AttachmentResponse(string Id, string FileName, string ContentType, long SizeBytes, DateTimeOffset CreatedAt);
public sealed record StoredAttachment(string FileName, string ContentType, long SizeBytes, string StoragePath);
public sealed record AttachmentFile(Stream Content, string FileName, string ContentType, long SizeBytes);

public interface IAttachmentStorage
{
    Task<StoredAttachment> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        long maxSizeBytes,
        CancellationToken ct);

    Task<Stream> OpenReadAsync(string storagePath, string contentType, CancellationToken ct);
    Task DeleteAsync(string storagePath, CancellationToken ct);
}
public sealed record WorkLogResponse(string Id, string UserId, decimal Hours, string? Note, DateTimeOffset CreatedAt);
public sealed record WorkItemRelationResponse(string RelatedWorkItemId, string RelationType);
public sealed record WorkItemApprovalResponse(
    string Id,
    string FromStatus,
    string ToStatus,
    string RequestedByUserId,
    DateTimeOffset RequestedAt,
    DateTimeOffset ExpiresAt,
    string Status,
    string? DecidedByUserId,
    DateTimeOffset? DecidedAt,
    string? Note,
    DateTimeOffset? ConsumedAt);
public sealed record ProjectSummaryResponse(int Total, int Done, int InProgress, int Overdue);
public sealed record StatusDistributionResponse(string Status, int Count);
public sealed record UserWorkloadResponse(string UserId, int OpenItems, int OverdueItems, decimal LoggedHours);
public sealed record DueDateRiskResponse(string Id, string Title, string? AssigneeUserId, DateTimeOffset DueDate, string Status);
public sealed record SprintBurndownPointResponse(DateOnly Date, decimal RemainingPoints, int RemainingItems);
public sealed record SprintVelocityResponse(string SprintId, int CompletedItems, decimal CompletedPoints);
public sealed record WorkItemStatusHistoryResponse(
    string? FromStatus,
    string ToStatus,
    string ChangedByUserId,
    DateTimeOffset ChangedAt);
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
    Task EnsureCanAsync(string userId, string projectId, string permission, CancellationToken ct);
}

public sealed record WorkItemRealtimeChange(
    string EventType,
    string WorkItemId,
    string ProjectId,
    string BoardId,
    WorkItemRealtimeItem WorkItem,
    string CorrelationId,
    DateTimeOffset OccurredAt);

public sealed record WorkItemRealtimeItem(
    string Id,
    string ProjectId,
    string BoardId,
    string ColumnId,
    string Title,
    string Type,
    string Priority,
    string Status,
    string? AssigneeUserId,
    DateTimeOffset? DueDate,
    string? SprintId,
    decimal EstimatePoints,
    DateTimeOffset? CompletedAt,
    long Rank);

public interface IWorkItemRealtimePublisher
{
    Task PublishAsync(WorkItemRealtimeChange change, CancellationToken ct);
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
        string fromStatus,
        string toStatus,
        CancellationToken ct);
}

public sealed record BoardPlacement(string ColumnId, string Status, bool EnforcesWipLimit);

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

public sealed class WorkItemDocument : IDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = string.Empty;
    public string BoardId { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public string? TeamId { get; set; }
    public string ColumnId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = "Task";
    public string Priority { get; set; } = "Medium";
    public string Status { get; set; } = "To Do";
    public long Rank { get; set; }
    public string? AssigneeUserId { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public DateTimeOffset? DueReminderSentAt { get; set; }
    public string? SprintId { get; set; }
    public decimal EstimatePoints { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public bool Archived { get; set; }
    public List<string> Labels { get; set; } = [];
    public List<ChecklistItemDocument> Checklist { get; set; } = [];
    public List<CommentDocument> Comments { get; set; } = [];
    public List<AttachmentDocument> Attachments { get; set; } = [];
    public List<WorkLogDocument> WorkLogs { get; set; } = [];
    public List<WorkItemRelationDocument> Relations { get; set; } = [];
    public List<WorkItemApprovalDocument> Approvals { get; set; } = [];
    public List<WorkItemStatusHistoryDocument> StatusHistory { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ChecklistItemDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = string.Empty;
    public bool Completed { get; set; }
}

public sealed class CommentDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Body { get; set; } = string.Empty;
    public string AuthorUserId { get; set; } = "system";
    public List<string> Mentions { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    public List<CommentRevisionDocument> History { get; set; } = [];
}

public sealed class CommentRevisionDocument
{
    public string Body { get; set; } = string.Empty;
    public string EditedByUserId { get; set; } = "system";
    public DateTimeOffset EditedAt { get; set; }
}

public sealed class AttachmentDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class WorkLogDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public decimal Hours { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class WorkItemRelationDocument
{
    public string RelatedWorkItemId { get; set; } = string.Empty;
    public string RelationType { get; set; } = "RelatesTo";
}

public sealed class WorkItemApprovalDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public string RequestedByUserId { get; set; } = "system";
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string Status { get; set; } = "Pending";
    public string? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}

public sealed class WorkItemStatusHistoryDocument
{
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string ChangedByUserId { get; set; } = "system";
    public DateTimeOffset ChangedAt { get; set; }
}

public sealed class WorkItemService(
    IDocumentRepository<WorkItemDocument> workItems,
    NotificationService notifications,
    AuditService audit,
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
    IWorkItemRealtimePublisher realtimePublisher,
    IWorkItemReadModelCache readModelCache,
    IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions)
{
    private const long RankStep = 1_000_000;

    public async Task<WorkItemResponse> CreateAsync(CreateWorkItemRequest request, string correlationId, CancellationToken ct)
    {
        await EnsurePermissionAsync(request.ProjectId, "WorkItemCreate", ct);

        if (string.IsNullOrWhiteSpace(request.ProjectId) || string.IsNullOrWhiteSpace(request.BoardId) || string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ValidationException("Project id, board id and title are required.");
        }

        if (request.Title.Length > 200)
        {
            throw new ValidationException("Work item title cannot exceed 200 characters.");
        }

        var type = NormalizeWorkItemType(request.Type);
        await using var structureLock = await AcquireRequiredLockAsync("project-structure:" + request.ProjectId, ct);
        var parent = await ValidateParentAsync(request.ProjectId, request.BoardId, type, request.ParentId, null, ct);
        var teamId = NormalizeOptionalId(request.TeamId);
        if (teamId is not null)
        {
            await teamPolicy.EnsureCanAssignAsync(request.ProjectId, teamId, request.AssigneeUserId, ct);
        }
        var placement = await boardPlacementPolicy.ResolveInitialAsync(request.ProjectId, request.BoardId, ct);
        var rank = await NextRankAsync(request.BoardId, placement.ColumnId, null, ct);
        var now = clock.UtcNow;
        var workItem = new WorkItemDocument
        {
            ProjectId = request.ProjectId,
            BoardId = request.BoardId,
            ParentId = parent?.Id,
            TeamId = teamId,
            ColumnId = placement.ColumnId,
            Title = request.Title.Trim(),
            Type = type,
            Priority = string.IsNullOrWhiteSpace(request.Priority) ? "Medium" : request.Priority,
            Status = placement.Status,
            Rank = rank,
            AssigneeUserId = request.AssigneeUserId,
            DueDate = request.DueDate,
            CreatedAt = now,
            UpdatedAt = now,
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
            await boardPlacementPolicy.EnsureHasCapacityAsync(
                request.BoardId,
                placement.ColumnId,
                null,
                ct);
            await workItems.CreateAsync(workItem, ct);
        }
        await searchIndex.IndexAsync(ToSearchRecord(workItem), ct);
        await audit.WriteAsync("WorkItemCreated", "WorkItem", workItem.Id, null, workItem.Title, correlationId, ct);
        await PublishRealtimeAsync("created", workItem, correlationId, ct);

        if (!string.IsNullOrWhiteSpace(workItem.AssigneeUserId))
        {
            await notifications.NotifyAsync(workItem.AssigneeUserId, "Assignment", $"Assigned to {workItem.Title}", ct);
        }

        await readModelCache.InvalidateProjectAsync(workItem.ProjectId, ct);
        return ToResponse(workItem);
    }

    public async Task<IReadOnlyList<WorkItemResponse>> SearchAsync(WorkItemSearchRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId))
        {
            throw new ValidationException("Project id is required for work item search.");
        }

        await EnsurePermissionAsync(request.ProjectId, "WorkItemView", ct);
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var text = request.Text?.Trim().ToLowerInvariant();
        if (text?.Length > 200)
        {
            throw new ValidationException("Search text cannot exceed 200 characters.");
        }

        if (!request.Archived && !string.IsNullOrWhiteSpace(text))
        {
            var ids = await searchIndex.SearchIdsAsync(
                new WorkItemSearchQuery(request.ProjectId, text, request.AssigneeUserId, request.Status, page, pageSize),
                ct);

            if (ids.Count == 0)
            {
                return [];
            }

            var idSet = ids.ToHashSet(StringComparer.Ordinal);
            var indexedResult = await workItems.ListByFilterAsync(
                x => !x.Archived && x.ProjectId == request.ProjectId && idSet.Contains(x.Id),
                pageSize: 200,
                cancellationToken: ct);

            var resultById = indexedResult.ToDictionary(x => x.Id, StringComparer.Ordinal);
            return ids
                .Where(resultById.ContainsKey)
                .Select(id => ToResponse(resultById[id]))
                .ToList();
        }

        var result = await workItems.ListByFilterAsync(
            x => x.Archived == request.Archived
                && x.ProjectId == request.ProjectId
                && (string.IsNullOrEmpty(request.AssigneeUserId) || x.AssigneeUserId == request.AssigneeUserId)
                && (string.IsNullOrEmpty(request.Status) || x.Status == request.Status)
                && (string.IsNullOrEmpty(text) || x.Title.ToLower().Contains(text) || x.Description.ToLower().Contains(text)),
            x => x.Rank,
            page: page,
            pageSize: pageSize,
            cancellationToken: ct);

        return result.Select(ToResponse).ToList();
    }

    public async Task<WorkItemResponse> GetAsync(string id, CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemView", ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> UpdateAsync(string id, UpdateWorkItemRequest request, string correlationId, CancellationToken ct)
    {
        await using var workItemLock = await AcquireRequiredLockAsync("work-item:" + id, ct);
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
        await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
        await searchIndex.IndexAsync(ToSearchRecord(workItem), ct);
        await audit.WriteAsync("WorkItemUpdated", "WorkItem", workItem.Id, oldValue, $"{workItem.Title}|{workItem.Priority}|{workItem.DueDate:o}", correlationId, ct);
        await PublishRealtimeAsync("updated", workItem, correlationId, ct);
        await readModelCache.InvalidateProjectAsync(workItem.ProjectId, ct);
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
        await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
        await searchIndex.IndexAsync(ToSearchRecord(workItem), ct);
        await audit.WriteAsync("WorkItemAssigned", "WorkItem", workItem.Id, oldAssignee, request.AssigneeUserId, correlationId, ct);
        await notifications.NotifyAsync(request.AssigneeUserId, "Assignment", $"Assigned to {workItem.Title}", ct);
        await PublishRealtimeAsync("updated", workItem, correlationId, ct);
        await readModelCache.InvalidateProjectAsync(workItem.ProjectId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> SetTeamAsync(
        string id,
        SetWorkItemTeamRequest request,
        string correlationId,
        CancellationToken ct)
    {
        await using var workItemLock = await AcquireRequiredLockAsync("work-item:" + id, ct);
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
        await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
        await audit.WriteAsync("WorkItemTeamChanged", "WorkItem", workItem.Id, oldTeamId, teamId, correlationId, ct);
        await PublishRealtimeAsync("updated", workItem, correlationId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> RequestApprovalAsync(
        string id,
        RequestWorkItemApprovalRequest request,
        string correlationId,
        CancellationToken ct)
    {
        await using var workItemLock = await AcquireRequiredLockAsync("work-item:" + id, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemMove", ct);
        var target = request.TargetStatus?.Trim();
        if (string.IsNullOrWhiteSpace(target) || workItem.Status.Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Approval target status must differ from the current status.");
        }

        var rule = await workflowPolicy.EnsureTransitionAllowedAsync(workItem.ProjectId, workItem.Status, target, ct);
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
        await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
        await audit.WriteAsync("WorkItemApprovalRequested", "WorkItem", workItem.Id, null, approval.Id, correlationId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> DecideApprovalAsync(
        string id,
        string approvalId,
        DecideWorkItemApprovalRequest request,
        string correlationId,
        CancellationToken ct)
    {
        await using var workItemLock = await AcquireRequiredLockAsync("work-item:" + id, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemApprove", ct);
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
            await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
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
        await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
        await audit.WriteAsync("WorkItemApprovalDecided", "WorkItem", workItem.Id, "Pending", approval.Status, correlationId, ct);
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
        await using var workItemLock = await AcquireRequiredLockAsync("work-item:" + id, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemMove", ct);
        var target = request.Status.Trim();

        if (workItem.Status.Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("WORK_ITEM_ALREADY_IN_STATUS", "Work item is already in the requested status.");
        }

        var rule = await workflowPolicy.EnsureTransitionAllowedAsync(workItem.ProjectId, workItem.Status, target, ct);

        if (rule.RequiresCompletedChecklist && workItem.Checklist.Any(x => !x.Completed))
        {
            throw new ConflictException("CHECKLIST_INCOMPLETE", "All checklist items must be completed before moving to Done.");
        }

        if (rule.RequiresAssignee && string.IsNullOrWhiteSpace(workItem.AssigneeUserId))
        {
            throw new ConflictException("ASSIGNEE_REQUIRED", "Assignee is required for this transition.");
        }

        WorkItemApprovalDocument? consumedApproval = null;
        if (rule.RequiresApproval)
        {
            consumedApproval = workItem.Approvals
                .Where(x => x.Status == "Approved" && x.ConsumedAt is null && x.ExpiresAt > clock.UtcNow)
                .Where(x => x.FromStatus.Equals(workItem.Status, StringComparison.OrdinalIgnoreCase))
                .Where(x => x.ToStatus.Equals(rule.ToStatus, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.DecidedAt)
                .FirstOrDefault()
                ?? throw new ConflictException("WORK_ITEM_APPROVAL_REQUIRED", "An approved transition request is required.");
        }

        var placement = await boardPlacementPolicy.EnsureCanMoveAsync(
            workItem.ProjectId,
            workItem.BoardId,
            workItem.Id,
            target,
            ct);
        var targetRank = await NextRankAsync(workItem.BoardId, placement.ColumnId, workItem.Id, ct);

        if (rule.ToStatusCategory.Equals("Done", StringComparison.OrdinalIgnoreCase))
        {
            await EnsureCanCompleteAsync(workItem, ct);
        }

        var oldStatus = string.Empty;
        await using (await AcquirePlacementLockAsync(workItem.BoardId, placement, ct))
        {
            await boardPlacementPolicy.EnsureHasCapacityAsync(
                workItem.BoardId,
                placement.ColumnId,
                workItem.Id,
                ct);
            oldStatus = workItem.Status;
            var now = clock.UtcNow;
            workItem.Status = placement.Status;
            workItem.ColumnId = placement.ColumnId;
            workItem.Rank = targetRank;
            workItem.CompletedAt = rule.ToStatusCategory.Equals("Done", StringComparison.OrdinalIgnoreCase) ? now : null;
            ApplyTransitionAutomations(workItem, rule.Automations, currentUser.UserId ?? "system");
            if (consumedApproval is not null)
            {
                consumedApproval.ConsumedAt = now;
            }

            foreach (var staleApproval in workItem.Approvals.Where(x =>
                x.Id != consumedApproval?.Id
                && x.ConsumedAt is null
                && x.FromStatus.Equals(oldStatus, StringComparison.OrdinalIgnoreCase)
                && x.Status is "Pending" or "Approved"))
            {
                staleApproval.Status = "Cancelled";
            }
            workItem.StatusHistory.Add(new WorkItemStatusHistoryDocument
            {
                FromStatus = oldStatus,
                ToStatus = placement.Status,
                ChangedByUserId = currentUser.UserId ?? "system",
                ChangedAt = now
            });
            workItem.UpdatedAt = now;
            await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
        }
        await searchIndex.IndexAsync(ToSearchRecord(workItem), ct);
        await audit.WriteAsync("WorkItemMoved", "WorkItem", workItem.Id, oldStatus, placement.Status, correlationId, ct);
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
        await readModelCache.InvalidateProjectAsync(workItem.ProjectId, ct);
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
        await using var workItemLock = await AcquireRequiredLockAsync("work-item:" + id, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemMove", ct);

        var rank = await ResolveReorderRankAsync(workItem, request, ct);
        var oldRank = workItem.Rank;
        workItem.Rank = rank;
        workItem.UpdatedAt = clock.UtcNow;
        await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
        await audit.WriteAsync(
            "WorkItemReordered",
            "WorkItem",
            workItem.Id,
            oldRank.ToString(System.Globalization.CultureInfo.InvariantCulture),
            rank.ToString(System.Globalization.CultureInfo.InvariantCulture),
            correlationId,
            ct);
        await PublishRealtimeAsync("reordered", workItem, correlationId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> SetPlanningAsync(string id, SetWorkItemPlanningRequest request, CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemUpdate", ct);

        if (request.EstimatePoints is < 0 or > 1000)
        {
            throw new ValidationException("Estimate points must be between 0 and 1000.");
        }

        workItem.SprintId = string.IsNullOrWhiteSpace(request.SprintId) ? null : request.SprintId.Trim();
        workItem.EstimatePoints = request.EstimatePoints ?? workItem.EstimatePoints;
        workItem.UpdatedAt = clock.UtcNow;
        await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
        await searchIndex.IndexAsync(ToSearchRecord(workItem), ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> AddChecklistItemAsync(string id, AddChecklistItemRequest request, CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemUpdate", ct);
        workItem.Checklist.Add(new ChecklistItemDocument { Text = request.Text.Trim() });
        workItem.UpdatedAt = clock.UtcNow;
        await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
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
        await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
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
        await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
        await searchIndex.IndexAsync(ToSearchRecord(workItem), ct);
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
        await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
        await searchIndex.IndexAsync(ToSearchRecord(workItem), ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> AddCommentAsync(string id, AddCommentRequest request, string correlationId, CancellationToken ct)
    {
        var body = NormalizeCommentBody(request.Body);
        var mentions = NormalizeMentions(request.Mentions);
        await using var workItemLock = await AcquireRequiredLockAsync("work-item:" + id, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "CommentCreate", ct);
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

        workItem.Comments.Add(comment);
        workItem.UpdatedAt = clock.UtcNow;
        await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
        await audit.WriteAsync("WorkItemCommentAdded", "WorkItem", workItem.Id, null, comment.Id, correlationId, ct);

        foreach (var mentionedUserId in comment.Mentions)
        {
            await notifications.NotifyAsync(mentionedUserId, "Mention", $"Mentioned on {workItem.Title}", ct);
        }

        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> EditCommentAsync(string id, string commentId, EditCommentRequest request, string correlationId, CancellationToken ct)
    {
        var body = NormalizeCommentBody(request.Body);
        await using var workItemLock = await AcquireRequiredLockAsync("work-item:" + id, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "CommentCreate", ct);
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
        workItem.UpdatedAt = now;
        await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
        await audit.WriteAsync("WorkItemCommentEdited", "WorkItem", workItem.Id, oldValue, comment.Id, correlationId, ct);
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> DeleteCommentAsync(string id, string commentId, string correlationId, CancellationToken ct)
    {
        await using var workItemLock = await AcquireRequiredLockAsync("work-item:" + id, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "CommentCreate", ct);
        var comment = workItem.Comments.SingleOrDefault(x => x.Id == commentId)
            ?? throw new NotFoundException("COMMENT_NOT_FOUND", "Comment was not found.");

        if (!string.Equals(comment.AuthorUserId, currentUser.UserId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Only the comment author can delete this comment.");
        }

        workItem.Comments.Remove(comment);
        workItem.UpdatedAt = clock.UtcNow;
        await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
        await audit.WriteAsync("WorkItemCommentDeleted", "WorkItem", workItem.Id, comment.Body, null, correlationId, ct);
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
            CreatedAt = clock.UtcNow
        };
        workItem.Attachments.Add(attachment);
        workItem.UpdatedAt = clock.UtcNow;
        try
        {
            await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
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
        return ToResponse(workItem);
    }

    public async Task<AttachmentFile> OpenAttachmentAsync(string id, string attachmentId, CancellationToken ct)
    {
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemView", ct);
        var attachment = workItem.Attachments.SingleOrDefault(x => x.Id == attachmentId)
            ?? throw new NotFoundException("ATTACHMENT_NOT_FOUND", "Attachment was not found.");
        var content = await attachmentStorage.OpenReadAsync(attachment.StoragePath, attachment.ContentType, ct);
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
        var attachment = workItem.Attachments.SingleOrDefault(x => x.Id == attachmentId)
            ?? throw new NotFoundException("ATTACHMENT_NOT_FOUND", "Attachment was not found.");
        workItem.Attachments.Remove(attachment);

        workItem.UpdatedAt = clock.UtcNow;
        await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
        try
        {
            await attachmentStorage.DeleteAsync(attachment.StoragePath, ct);
        }
        catch
        {
            workItem.Attachments.Add(attachment);
            await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, CancellationToken.None);
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
        return ToResponse(workItem);
    }

    public async Task<WorkItemResponse> AddWorkLogAsync(string id, AddWorkLogRequest request, CancellationToken ct)
    {
        if (request.Hours <= 0 || request.Hours > 24)
        {
            throw new ValidationException("Work log hours must be between 0 and 24.");
        }

        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkLogCreate", ct);
        workItem.WorkLogs.Add(new WorkLogDocument
        {
            UserId = request.UserId,
            Hours = request.Hours,
            Note = request.Note,
            CreatedAt = clock.UtcNow
        });
        workItem.UpdatedAt = clock.UtcNow;
        await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
        await readModelCache.InvalidateProjectAsync(workItem.ProjectId, ct);
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
        await using var workItemLock = await AcquireRequiredLockAsync("work-item:" + id, ct);
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
        await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
        await audit.WriteAsync("WorkItemParentChanged", "WorkItem", workItem.Id, oldParentId, parent?.Id, correlationId, ct);
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
        await using var workItemLock = await AcquireRequiredLockAsync("work-item:" + id, ct);
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

        if (relationType is "Blocks" or "BlockedBy")
        {
            await EnsureDependencyDoesNotCycleAsync(workItem, relatedWorkItem, relationType, ct);
        }

        workItem.Relations.Add(new WorkItemRelationDocument
        {
            RelatedWorkItemId = relatedWorkItem.Id,
            RelationType = relationType
        });
        workItem.UpdatedAt = clock.UtcNow;
        await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
        await audit.WriteAsync("WorkItemLinked", "WorkItem", workItem.Id, null, $"{relationType}:{relatedWorkItem.Id}", correlationId, ct);
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
        await using var workItemLock = await AcquireRequiredLockAsync("work-item:" + id, ct);
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

        workItem.UpdatedAt = clock.UtcNow;
        await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
        await audit.WriteAsync("WorkItemUnlinked", "WorkItem", workItem.Id, $"{normalizedType}:{relatedWorkItemId}", null, correlationId, ct);
        return ToResponse(workItem);
    }

    public async Task ArchiveAsync(string id, string correlationId, CancellationToken ct)
    {
        var initialWorkItem = await GetWorkItem(id, ct);
        await using var structureLock = await AcquireRequiredLockAsync("project-structure:" + initialWorkItem.ProjectId, ct);
        await using var workItemLock = await AcquireRequiredLockAsync("work-item:" + id, ct);
        var workItem = await GetWorkItem(id, ct);
        await EnsurePermissionAsync(workItem.ProjectId, "WorkItemDelete", ct);
        await EnsureHasNoActiveChildrenAsync(workItem.Id, ct);
        workItem.Archived = true;
        workItem.UpdatedAt = clock.UtcNow;
        await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
        await searchIndex.DeleteAsync(workItem.Id, ct);
        await audit.WriteAsync("WorkItemArchived", "WorkItem", workItem.Id, "active", "archived", correlationId, ct);
        await PublishRealtimeAsync("archived", workItem, correlationId, ct);
        await readModelCache.InvalidateProjectAsync(workItem.ProjectId, ct);
    }

    public async Task<WorkItemResponse> RestoreAsync(string id, string correlationId, CancellationToken ct)
    {
        var initialWorkItem = await GetArchivedWorkItem(id, ct);
        await using var structureLock = await AcquireRequiredLockAsync("project-structure:" + initialWorkItem.ProjectId, ct);
        await using var workItemLock = await AcquireRequiredLockAsync("work-item:" + id, ct);
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
            await boardPlacementPolicy.EnsureHasCapacityAsync(
                workItem.BoardId,
                placement.ColumnId,
                workItem.Id,
                ct);
            workItem.ColumnId = placement.ColumnId;
            workItem.Status = placement.Status;
            workItem.Rank = await NextRankAsync(workItem.BoardId, placement.ColumnId, workItem.Id, ct);
            workItem.Archived = false;
            workItem.UpdatedAt = clock.UtcNow;
            await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id && x.Archived, workItem, ct);
        }

        await searchIndex.IndexAsync(ToSearchRecord(workItem), ct);
        await audit.WriteAsync("WorkItemRestored", "WorkItem", workItem.Id, "archived", "active", correlationId, ct);
        await PublishRealtimeAsync("restored", workItem, correlationId, ct);
        await readModelCache.InvalidateProjectAsync(workItem.ProjectId, ct);
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
                clock.UtcNow),
            ct);

    public async Task<ProjectSummaryResponse> ProjectSummaryAsync(string projectId, CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, "WorkItemView", ct);
        return await readModelCache.GetOrCreateAsync(
            projectId,
            "project-summary",
            ReadModelTtl,
            async token =>
            {
                var result = await LoadActiveProjectItemsAsync(projectId, token);
                var now = clock.UtcNow;
                return new ProjectSummaryResponse(
                    result.Count,
                    result.Count(x => x.CompletedAt is not null),
                    result.Count(x => x.CompletedAt is null && x.Status is "In Progress" or "Code Review" or "Test"),
                    result.Count(x => x.DueDate < now && x.CompletedAt is null));
            },
            ct);
    }

    public async Task<IReadOnlyList<StatusDistributionResponse>> StatusDistributionAsync(string projectId, CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, "WorkItemView", ct);
        return await readModelCache.GetOrCreateAsync<IReadOnlyList<StatusDistributionResponse>>(
            projectId,
            "status-distribution",
            ReadModelTtl,
            async token => (await LoadActiveProjectItemsAsync(projectId, token))
                .GroupBy(x => x.Status)
                .OrderBy(x => x.Key)
                .Select(x => new StatusDistributionResponse(x.Key, x.Count()))
                .ToList(),
            ct);
    }

    public async Task<IReadOnlyList<UserWorkloadResponse>> UserWorkloadAsync(string projectId, CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, "WorkItemView", ct);
        return await readModelCache.GetOrCreateAsync<IReadOnlyList<UserWorkloadResponse>>(
            projectId,
            "user-workload",
            ReadModelTtl,
            async token =>
            {
                var now = clock.UtcNow;
                var result = await LoadActiveProjectItemsAsync(projectId, token);
                return result
                    .Where(x => !string.IsNullOrWhiteSpace(x.AssigneeUserId))
                    .GroupBy(x => x.AssigneeUserId!)
                    .OrderBy(x => x.Key)
                    .Select(x => new UserWorkloadResponse(
                        x.Key,
                        x.Count(item => item.CompletedAt is null),
                        x.Count(item => item.DueDate < now && item.CompletedAt is null),
                        x.SelectMany(item => item.WorkLogs).Sum(log => log.Hours)))
                    .ToList();
            },
            ct);
    }

    public async Task<IReadOnlyList<DueDateRiskResponse>> DueDateRisksAsync(string projectId, int days, CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, "WorkItemView", ct);
        var now = clock.UtcNow;
        var until = now.AddDays(Math.Clamp(days, 1, 90));
        var result = await workItems.ListByFilterAsync(
            x => x.ProjectId == projectId && !x.Archived && x.CompletedAt == null && x.DueDate <= until,
            x => x.DueDate!,
            pageSize: 1000,
            cancellationToken: ct);

        return result
            .Where(x => x.DueDate is not null)
            .Select(x => new DueDateRiskResponse(x.Id, x.Title, x.AssigneeUserId, x.DueDate!.Value, x.Status))
            .ToList();
    }

    public async Task<IReadOnlyList<SprintBurndownPointResponse>> SprintBurndownAsync(
        string projectId,
        string sprintId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, "WorkItemView", ct);

        if (string.IsNullOrWhiteSpace(sprintId))
        {
            throw new ValidationException("Sprint id is required.");
        }

        if (endDate < startDate)
        {
            throw new ValidationException("Sprint end date must be after start date.");
        }

        var days = endDate.DayNumber - startDate.DayNumber + 1;
        if (days > 60)
        {
            throw new ValidationException("Sprint burndown range cannot exceed 60 days.");
        }

        var result = await workItems.ListByFilterAsync(
            x => x.ProjectId == projectId && x.SprintId == sprintId && !x.Archived,
            pageSize: 1000,
            cancellationToken: ct);

        return Enumerable.Range(0, days)
            .Select(offset =>
            {
                var date = startDate.AddDays(offset);
                var endOfDay = new DateTimeOffset(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
                var remaining = result.Where(item => item.CompletedAt is null || item.CompletedAt > endOfDay).ToList();
                return new SprintBurndownPointResponse(
                    date,
                    remaining.Sum(item => item.EstimatePoints),
                    remaining.Count);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<SprintVelocityResponse>> SprintVelocityAsync(string projectId, int sprintCount, CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, "WorkItemView", ct);
        var result = await workItems.ListByFilterAsync(
            x => x.ProjectId == projectId && !x.Archived && x.SprintId != null,
            pageSize: 2000,
            cancellationToken: ct);

        return result
            .GroupBy(x => x.SprintId!)
            .OrderByDescending(x => x.Max(item => item.UpdatedAt))
            .Take(Math.Clamp(sprintCount, 1, 12))
            .Select(x => new SprintVelocityResponse(
                x.Key,
                x.Count(item => item.CompletedAt is not null),
                x.Where(item => item.CompletedAt is not null).Sum(item => item.EstimatePoints)))
            .ToList();
    }

    public async Task<FlowTimeReportResponse> FlowTimeAsync(
        string projectId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, "WorkItemView", ct);

        var reportTo = to ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var reportFrom = from ?? reportTo.AddDays(-29);
        if (reportTo < reportFrom)
        {
            throw new ValidationException("Report end date must be after start date.");
        }

        if (reportTo.DayNumber - reportFrom.DayNumber + 1 > 366)
        {
            throw new ValidationException("Flow time report range cannot exceed 366 days.");
        }

        var fromInstant = new DateTimeOffset(reportFrom.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toInstant = new DateTimeOffset(reportTo.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var completed = await workItems.ListByFilterAsync(
            x => x.ProjectId == projectId
                && !x.Archived
                && x.CompletedAt != null
                && x.CompletedAt >= fromInstant
                && x.CompletedAt <= toInstant,
            pageSize: 5000,
            cancellationToken: ct);

        var leadTimes = completed
            .Select(x => Math.Max(0, (x.CompletedAt!.Value - x.CreatedAt).TotalHours))
            .ToList();
        var cycleTimes = completed
            .Select(TryCalculateCycleTimeHours)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToList();

        return new FlowTimeReportResponse(
            reportFrom,
            reportTo,
            completed.Count,
            cycleTimes.Count,
            Average(leadTimes) ?? 0,
            Median(leadTimes) ?? 0,
            Average(cycleTimes),
            Median(cycleTimes));
    }

    public async Task<TaskCompletionRateResponse> CompletionRateAsync(
        string projectId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, "WorkItemView", ct);
        var range = NormalizeReportRange(from, to);
        var items = await workItems.ListByFilterAsync(
            x => x.ProjectId == projectId
                && !x.Archived
                && x.CreatedAt >= range.FromInstant
                && x.CreatedAt <= range.ToInstant,
            pageSize: 5000,
            cancellationToken: ct);
        var completed = items.Count(x => x.CompletedAt is not null && x.CompletedAt <= range.ToInstant);
        return new TaskCompletionRateResponse(
            range.From,
            range.To,
            items.Count,
            completed,
            items.Count == 0 ? 0 : Math.Round(completed * 100d / items.Count, 2));
    }

    public async Task<IReadOnlyList<TeamPerformanceResponse>> TeamPerformanceAsync(
        string projectId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, "WorkItemView", ct);
        var range = NormalizeReportRange(from, to);
        var teams = await teamPolicy.ListProjectTeamsAsync(projectId, ct);
        var items = await workItems.ListByFilterAsync(
            x => x.ProjectId == projectId
                && !x.Archived
                && x.TeamId != null
                && x.CreatedAt >= range.FromInstant
                && x.CreatedAt <= range.ToInstant,
            pageSize: 5000,
            cancellationToken: ct);

        return teams.OrderBy(x => x.Name).Select(team =>
        {
            var assigned = items.Where(x => x.TeamId == team.Id).ToList();
            var completed = assigned
                .Where(x => x.CompletedAt is not null && x.CompletedAt <= range.ToInstant)
                .ToList();
            var leadTimes = completed
                .Select(x => Math.Max(0, (x.CompletedAt!.Value - x.CreatedAt).TotalHours))
                .ToList();
            return new TeamPerformanceResponse(
                team.Id,
                team.Name,
                assigned.Count,
                completed.Count,
                assigned.Count == 0 ? 0 : Math.Round(completed.Count * 100d / assigned.Count, 2),
                Average(leadTimes),
                assigned.SelectMany(x => x.WorkLogs).Sum(x => x.Hours));
        }).ToList();
    }

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
            var workItem = await GetWorkItem(candidate.Id, ct);
            if (workItem.CompletedAt is not null
                || workItem.AssigneeUserId is null
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
            await workItems.ReplaceByFilterAsync(x => x.Id == workItem.Id, workItem, ct);
            sent++;
        }

        return sent;
    }

    private static double? TryCalculateCycleTimeHours(WorkItemDocument item)
    {
        if (item.CompletedAt is null)
        {
            return null;
        }

        var history = item.StatusHistory.OrderBy(x => x.ChangedAt).ToList();
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
        var normalizedType = NormalizeWorkItemType(type);
        if (string.IsNullOrWhiteSpace(parentId))
        {
            if (normalizedType == "Subtask")
            {
                throw new ValidationException("A subtask must have a parent work item.");
            }

            return null;
        }

        if (normalizedType == "Epic")
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

        var parentType = NormalizeWorkItemType(parent.Type);
        if (normalizedType == "Subtask")
        {
            if (parentType is not ("Story" or "Task" or "Bug"))
            {
                throw new ValidationException("A subtask parent must be a story, task or bug.");
            }

            if (!string.Equals(parent.BoardId, boardId, StringComparison.Ordinal))
            {
                throw new ValidationException("A subtask and its parent must belong to the same board.");
            }
        }
        else if (parentType != "Epic")
        {
            throw new ValidationException("A story, task or bug can only be parented by an epic.");
        }

        return parent;
    }

    private async Task EnsureCanCompleteAsync(WorkItemDocument workItem, CancellationToken ct)
    {
        await EnsureHasNoActiveChildrenAsync(workItem.Id, ct);
        var projectItems = await LoadActiveProjectItemsAsync(workItem.ProjectId, ct);
        var blockers = projectItems
            .Where(candidate => candidate.CompletedAt is null && !IsCompletedStatus(candidate.Status))
            .Where(candidate =>
                workItem.Relations.Any(relation =>
                    relation.RelationType.Equals("BlockedBy", StringComparison.OrdinalIgnoreCase)
                    && relation.RelatedWorkItemId == candidate.Id)
                || candidate.Relations.Any(relation =>
                    relation.RelationType.Equals("Blocks", StringComparison.OrdinalIgnoreCase)
                    && relation.RelatedWorkItemId == workItem.Id))
            .Select(x => x.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();

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

    private async Task EnsureDependencyDoesNotCycleAsync(
        WorkItemDocument source,
        WorkItemDocument target,
        string relationType,
        CancellationToken ct)
    {
        var projectItems = await LoadActiveProjectItemsAsync(source.ProjectId, ct);
        var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var item in projectItems)
        {
            foreach (var relation in item.Relations)
            {
                if (relation.RelationType.Equals("Blocks", StringComparison.OrdinalIgnoreCase))
                {
                    AddDependencyEdge(adjacency, item.Id, relation.RelatedWorkItemId);
                }
                else if (relation.RelationType.Equals("BlockedBy", StringComparison.OrdinalIgnoreCase))
                {
                    AddDependencyEdge(adjacency, relation.RelatedWorkItemId, item.Id);
                }
            }
        }

        var edgeFrom = relationType == "Blocks" ? source.Id : target.Id;
        var edgeTo = relationType == "Blocks" ? target.Id : source.Id;
        if (adjacency.TryGetValue(edgeFrom, out var existingTargets) && existingTargets.Contains(edgeTo))
        {
            throw new ConflictException("WORK_ITEM_DEPENDENCY_EXISTS", "The dependency already exists.");
        }

        if (HasDependencyPath(adjacency, edgeTo, edgeFrom))
        {
            throw new ConflictException("WORK_ITEM_DEPENDENCY_CYCLE", "The dependency would create a cycle.");
        }
    }

    private async Task<IReadOnlyList<WorkItemDocument>> LoadActiveProjectItemsAsync(string projectId, CancellationToken ct)
    {
        const int pageSize = 200;
        var page = 1;
        var result = new List<WorkItemDocument>();
        while (true)
        {
            var batch = await workItems.ListByFilterAsync(
                x => x.ProjectId == projectId && !x.Archived,
                page: page,
                pageSize: pageSize,
                cancellationToken: ct);
            result.AddRange(batch);
            if (batch.Count < pageSize)
            {
                return result;
            }

            page++;
        }
    }

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

    private static void AddDependencyEdge(
        IDictionary<string, HashSet<string>> adjacency,
        string from,
        string to)
    {
        if (!adjacency.TryGetValue(from, out var targets))
        {
            targets = new HashSet<string>(StringComparer.Ordinal);
            adjacency[from] = targets;
        }

        targets.Add(to);
    }

    private static bool HasDependencyPath(
        IReadOnlyDictionary<string, HashSet<string>> adjacency,
        string from,
        string to)
    {
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(from);
        while (pending.TryPop(out var current))
        {
            if (current == to)
            {
                return true;
            }

            if (!visited.Add(current) || !adjacency.TryGetValue(current, out var targets))
            {
                continue;
            }

            foreach (var target in targets)
            {
                pending.Push(target);
            }
        }

        return false;
    }

    private static string NormalizeWorkItemType(string? type)
    {
        var requested = string.IsNullOrWhiteSpace(type) ? "Task" : type.Trim();
        return requested.ToLowerInvariant() switch
        {
            "epic" => "Epic",
            "story" => "Story",
            "task" => "Task",
            "bug" => "Bug",
            "subtask" or "sub-task" => "Subtask",
            _ => throw new ValidationException("Work item type must be Epic, Story, Task, Bug or Subtask.")
        };
    }

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

    private static void ApplyTransitionAutomations(
        WorkItemDocument workItem,
        IReadOnlyCollection<WorkflowAutomationRule>? automations,
        string actorUserId)
    {
        foreach (var automation in automations ?? [])
        {
            switch (automation.Action)
            {
                case "AssignToActor":
                    workItem.AssigneeUserId = actorUserId;
                    break;
                case "ClearAssignee":
                    workItem.AssigneeUserId = null;
                    break;
                case "AddLabel" when automation.Value is not null:
                    if (!workItem.Labels.Contains(automation.Value, StringComparer.OrdinalIgnoreCase))
                    {
                        workItem.Labels.Add(automation.Value);
                    }
                    break;
                case "RemoveLabel" when automation.Value is not null:
                    workItem.Labels.RemoveAll(x => x.Equals(automation.Value, StringComparison.OrdinalIgnoreCase));
                    break;
                default:
                    throw new ConflictException("WORKFLOW_AUTOMATION_INVALID", "Workflow contains an unsupported automation.");
            }
        }
    }

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

    private async Task<WorkItemDocument> GetWorkItem(string id, CancellationToken ct) =>
        await workItems.SelectAsync(x => x.Id == id && !x.Archived, ct)
        ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");

    private async Task<WorkItemDocument> GetArchivedWorkItem(string id, CancellationToken ct) =>
        await workItems.SelectAsync(x => x.Id == id && x.Archived, ct)
        ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Archived work item was not found.");

    private async Task EnsurePermissionAsync(string projectId, string permission, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        await permissionChecker.EnsureCanAsync(userId, projectId, permission, ct);
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

    private async Task<long> NextRankAsync(
        string boardId,
        string columnId,
        string? ignoredWorkItemId,
        CancellationToken ct)
    {
        var last = await workItems.ListByFilterAsync(
            x => !x.Archived
                && x.BoardId == boardId
                && x.ColumnId == columnId
                && (ignoredWorkItemId == null || x.Id != ignoredWorkItemId),
            x => x.Rank,
            orderDescending: true,
            pageSize: 1,
            cancellationToken: ct);
        if (last.Count == 0)
        {
            return RankStep;
        }

        try
        {
            return checked(last[0].Rank + RankStep);
        }
        catch (OverflowException)
        {
            throw new ConflictException(
                "WORK_ITEM_RANK_EXHAUSTED",
                "The target column rank range is exhausted; rebalance the column before retrying.");
        }
    }

    private async Task<long> ResolveReorderRankAsync(
        WorkItemDocument workItem,
        ReorderWorkItemRequest request,
        CancellationToken ct)
    {
        var beforeId = NormalizeOptionalId(request.BeforeWorkItemId);
        var afterId = NormalizeOptionalId(request.AfterWorkItemId);
        if ((beforeId is null) == (afterId is null))
        {
            throw new ValidationException("Exactly one before or after work item id is required.");
        }

        var anchorId = beforeId ?? afterId!;
        if (anchorId == workItem.Id)
        {
            throw new ValidationException("A work item cannot be ordered relative to itself.");
        }

        var anchor = await GetWorkItem(anchorId, ct);
        if (anchor.ProjectId != workItem.ProjectId
            || anchor.BoardId != workItem.BoardId
            || anchor.ColumnId != workItem.ColumnId
            || anchor.Status != workItem.Status)
        {
            throw new ValidationException("The rank anchor must be in the same board column.");
        }

        if (beforeId is not null)
        {
            var predecessors = await workItems.ListByFilterAsync(
                x => !x.Archived
                    && x.BoardId == workItem.BoardId
                    && x.ColumnId == workItem.ColumnId
                    && x.Id != workItem.Id
                    && x.Id != anchor.Id
                    && x.Rank < anchor.Rank,
                x => x.Rank,
                orderDescending: true,
                pageSize: 1,
                cancellationToken: ct);
            var lower = predecessors.Count == 0 ? checked(anchor.Rank - RankStep) : predecessors[0].Rank;
            return RankBetween(lower, anchor.Rank);
        }

        var successors = await workItems.ListByFilterAsync(
            x => !x.Archived
                && x.BoardId == workItem.BoardId
                && x.ColumnId == workItem.ColumnId
                && x.Id != workItem.Id
                && x.Id != anchor.Id
                && x.Rank > anchor.Rank,
            x => x.Rank,
            pageSize: 1,
            cancellationToken: ct);
        var upper = successors.Count == 0 ? checked(anchor.Rank + RankStep) : successors[0].Rank;
        return RankBetween(anchor.Rank, upper);
    }

    private static long RankBetween(long lower, long upper)
    {
        if ((decimal)upper - lower <= 1)
        {
            throw new ConflictException(
                "WORK_ITEM_RANK_EXHAUSTED",
                "No rank space remains between the selected work items; rebalance the column before retrying.");
        }

        return (long)(((decimal)lower + upper) / 2);
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
            item.Attachments.Select(x => new AttachmentResponse(x.Id, x.FileName, x.ContentType, x.SizeBytes, x.CreatedAt)).ToList(),
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
            item.Archived);

    private static WorkItemSearchRecord ToSearchRecord(WorkItemDocument item) =>
        new(
            item.Id,
            item.ProjectId,
            item.BoardId,
            item.Title,
            item.Description,
            item.Status,
            item.Priority,
            item.AssigneeUserId,
            item.Labels);

    private static WorkItemRealtimeItem ToRealtimeItem(WorkItemDocument item) =>
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
            item.Rank);
}
