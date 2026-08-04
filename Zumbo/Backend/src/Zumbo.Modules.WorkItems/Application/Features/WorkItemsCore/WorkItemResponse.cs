using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

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
