using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Workflows;

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
