using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Workflows;

public sealed class WorkflowIssueTypeSchemeDocument
{
    public string IssueType { get; set; } = "*";
    public string DefaultStatus { get; set; } = string.Empty;
    public List<string> Statuses { get; set; } = [];
    public List<string> DoneStatuses { get; set; } = [];
}
