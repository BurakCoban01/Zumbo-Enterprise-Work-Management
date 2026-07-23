using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemDocument : IVersionedDocument
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
    public int IssueTypeSchemaVersion { get; set; } = 1;
    public List<WorkItemCustomFieldValueDocument> CustomFields { get; set; } = [];
    public string Priority { get; set; } = "Medium";
    public string Status { get; set; } = "To Do";
    public long Rank { get; set; }
    public string? RankRebalanceToken { get; set; }
    public string? AssigneeUserId { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public DateTimeOffset? DueReminderSentAt { get; set; }
    public string? SprintId { get; set; }
    public string? SourceTemplateId { get; set; }
    public string? SourceRecurrenceId { get; set; }
    public string? SourceIntakeSubmissionId { get; set; }
    public DateTimeOffset? RecurrenceScheduledForUtc { get; set; }
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
    public int ActivityStorageVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
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
    public string ChecksumSha256 { get; set; } = string.Empty;
    public string SecurityState { get; set; } = AttachmentSecurityStates.Clean;
    public string ScanProvider { get; set; } = "Legacy";
    public string? ScanDetail { get; set; }
    public DateTimeOffset? ScannedAt { get; set; }
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
