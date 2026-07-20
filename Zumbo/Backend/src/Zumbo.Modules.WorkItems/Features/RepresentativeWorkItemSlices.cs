using Zumbo.BuildingBlocks.Application.Persistence;
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
    string? TeamId = null,
    IReadOnlyCollection<WorkItemCustomFieldValueRequest>? CustomFields = null);

public sealed record WorkItemSearchRequest(
    string? ProjectId,
    string? AssigneeUserId,
    string? Status,
    string? Text,
    int Page = 1,
    int PageSize = 100,
    bool Archived = false,
    string? IssueType = null,
    string? CustomFieldKey = null,
    string? CustomFieldValue = null);

public sealed record WorkItemSearchPageResponse(
    IReadOnlyList<WorkItemResponse> Items,
    long TotalCount,
    bool Degraded);

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
    bool Archived = false,
    long Version = 0,
    int IssueTypeSchemaVersion = 1,
    IReadOnlyCollection<WorkItemCustomFieldValueResponse>? CustomFields = null) : IVersionedResource;

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
public sealed record AttachmentResponse(
    string Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    string SecurityState = AttachmentSecurityStates.Clean,
    string ScanProvider = "Legacy",
    DateTimeOffset? ScannedAt = null);
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

public sealed record WorkItemStatusHistoryResponse(
    string? FromStatus,
    string ToStatus,
    string ChangedByUserId,
    DateTimeOffset ChangedAt);

public sealed class CreateWorkItemValidator
{
    public static void Validate(CreateWorkItemRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId)
            || string.IsNullOrWhiteSpace(request.BoardId)
            || string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ValidationException("Project id, board id and title are required.");
        }

        if (request.Title.Length > 200)
        {
            throw new ValidationException("Work item title cannot exceed 200 characters.");
        }
    }
}

public sealed class CreateWorkItemHandler(WorkItemService service)
{
    public Task<WorkItemResponse> HandleAsync(CreateWorkItemRequest request, string correlationId, CancellationToken ct) =>
        service.CreateAsync(request, correlationId, ct);
}

public sealed class SearchWorkItemsValidator
{
    public static void Validate(WorkItemSearchRequest request)
    {
        ValidateProjectScope(request);
        ValidateText(request);
        if (request.CustomFieldKey?.Trim().Length > 40 || request.CustomFieldValue?.Trim().Length > 4_000)
        {
            throw new ValidationException("Custom field search filter is too long.");
        }
        if (string.IsNullOrWhiteSpace(request.CustomFieldKey) != string.IsNullOrWhiteSpace(request.CustomFieldValue))
        {
            throw new ValidationException("Custom field key and value must be supplied together.");
        }
    }

    public static void ValidateProjectScope(WorkItemSearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId))
        {
            throw new ValidationException("Project id is required for work item search.");
        }
    }

    public static void ValidateText(WorkItemSearchRequest request)
    {
        if (request.Text?.Trim().Length > 200)
        {
            throw new ValidationException("Search text cannot exceed 200 characters.");
        }
    }
}

public sealed class SearchWorkItemsHandler(WorkItemService service)
{
    public Task<IReadOnlyList<WorkItemResponse>> HandleAsync(WorkItemSearchRequest request, CancellationToken ct) =>
        service.SearchAsync(request, ct);
}
