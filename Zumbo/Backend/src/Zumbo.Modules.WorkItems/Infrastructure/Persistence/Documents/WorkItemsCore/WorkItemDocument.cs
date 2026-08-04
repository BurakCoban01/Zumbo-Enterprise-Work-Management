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
