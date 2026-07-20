using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Workflows;

public sealed class WorkflowDefinitionDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = string.Empty;
    public List<WorkflowStatusDocument> Statuses { get; set; } = [];
    public List<WorkflowTransitionDocument> Transitions { get; set; } = [];
    public List<WorkflowIssueTypeSchemeDocument> IssueTypeSchemes { get; set; } = [];
    public WorkflowVersionDocument? Draft { get; set; }
    public List<WorkflowVersionDocument> PublishedVersions { get; set; } = [];
    public int PublishedVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class WorkflowVersionDocument
{
    public int Number { get; set; }
    public string State { get; set; } = "Draft";
    public List<WorkflowStatusDocument> Statuses { get; set; } = [];
    public List<WorkflowTransitionDocument> Transitions { get; set; } = [];
    public List<WorkflowIssueTypeSchemeDocument> IssueTypeSchemes { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public sealed class WorkflowStatusDocument
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}

public sealed class WorkflowTransitionDocument
{
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public bool RequiresAssignee { get; set; }
    public bool RequiresCompletedChecklist { get; set; }
    public bool RequiresApproval { get; set; }
    public List<WorkflowAutomationDocument> Automations { get; set; } = [];
}

public sealed class WorkflowAutomationDocument
{
    public string Action { get; set; } = string.Empty;
    public string? Value { get; set; }
}

public sealed class WorkflowIssueTypeSchemeDocument
{
    public string IssueType { get; set; } = "*";
    public string DefaultStatus { get; set; } = string.Empty;
    public List<string> Statuses { get; set; } = [];
    public List<string> DoneStatuses { get; set; } = [];
}
