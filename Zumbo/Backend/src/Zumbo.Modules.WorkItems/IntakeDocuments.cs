using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public static class IntakeFormStates
{
    public const string Draft = "Draft";
    public const string Published = "Published";
    public const string Archived = "Archived";
}

public static class IntakeAccessPolicies
{
    public const string Internal = "Internal";
    public const string Public = "Public";
}

public static class IntakeFieldTypes
{
    public const string Text = "Text";
    public const string LongText = "LongText";
    public const string Email = "Email";
    public const string Number = "Number";
    public const string Date = "Date";
    public const string Choice = "Choice";
    public const string Checkbox = "Checkbox";
    public const string Attachment = "Attachment";
}

public static class IntakeSubmissionStates
{
    public const string Processing = "Processing";
    public const string New = "New";
    public const string InReview = "InReview";
    public const string Resolved = "Resolved";
    public const string Rejected = "Rejected";
}

public sealed class IntakeFormDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string State { get; set; } = IntakeFormStates.Draft;
    public string PublicId { get; set; } = Guid.NewGuid().ToString("N");
    public int PublishedVersion { get; set; }
    public string? PublishedAccessPolicy { get; set; }
    public IntakeFormDefinitionDocument Draft { get; set; } = new();
    public string CreatedByUserId { get; set; } = string.Empty;
    public string UpdatedByUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public long Version { get; set; }
}

public sealed class IntakeFormVersionDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string FormId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public int DefinitionVersion { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IntakeFormDefinitionDocument Definition { get; set; } = new();
    public string PublishedByUserId { get; set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; set; }
    public long Version { get; set; }
}

public sealed class IntakeFormDefinitionDocument
{
    public string AccessPolicy { get; set; } = IntakeAccessPolicies.Internal;
    public string BoardId { get; set; } = string.Empty;
    public string WorkItemType { get; set; } = "Task";
    public string DefaultPriority { get; set; } = "Medium";
    public string ConfirmationMessage { get; set; } = "Your request has been received.";
    public List<IntakeFieldDefinitionDocument> Fields { get; set; } = [];
    public IntakeFieldMappingDocument Mapping { get; set; } = new();
}

public sealed class IntakeFieldDefinitionDocument
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = IntakeFieldTypes.Text;
    public bool Required { get; set; }
    public string? HelpText { get; set; }
    public List<string> Options { get; set; } = [];
}

public sealed class IntakeFieldMappingDocument
{
    public string TitleFieldKey { get; set; } = string.Empty;
    public string? DescriptionFieldKey { get; set; }
    public string? PriorityFieldKey { get; set; }
    public string? DueDateFieldKey { get; set; }
    public List<IntakeCustomFieldMappingDocument> CustomFields { get; set; } = [];
}

public sealed class IntakeCustomFieldMappingDocument
{
    public string IntakeFieldKey { get; set; } = string.Empty;
    public string WorkItemFieldKey { get; set; } = string.Empty;
}

public sealed class IntakeSubmissionDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string FormId { get; set; } = string.Empty;
    public int FormVersion { get; set; }
    public string ProjectId { get; set; } = string.Empty;
    public string BoardId { get; set; } = string.Empty;
    public string AccessPolicy { get; set; } = string.Empty;
    public string SubmittedByUserId { get; set; } = "public";
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public string ConfirmationCode { get; set; } = string.Empty;
    public string State { get; set; } = IntakeSubmissionStates.Processing;
    public string WorkItemId { get; set; } = string.Empty;
    public List<IntakeSubmissionValueDocument> Values { get; set; } = [];
    public List<IntakeSubmissionAttachmentDocument> Attachments { get; set; } = [];
    public string? TriageNote { get; set; }
    public string? TriagedByUserId { get; set; }
    public DateTimeOffset? TriagedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class IntakeSubmissionValueDocument
{
    public string FieldKey { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class IntakeSubmissionAttachmentDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FieldKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string ChecksumSha256 { get; set; } = string.Empty;
    public string SecurityState { get; set; } = AttachmentSecurityStates.Clean;
    public string ScanProvider { get; set; } = string.Empty;
    public string? ScanDetail { get; set; }
    public DateTimeOffset? ScannedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
