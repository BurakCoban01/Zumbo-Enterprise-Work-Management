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
