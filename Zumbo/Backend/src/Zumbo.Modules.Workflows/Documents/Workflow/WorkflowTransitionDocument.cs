using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Workflows;

public sealed class WorkflowTransitionDocument
{
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public bool RequiresAssignee { get; set; }
    public bool RequiresCompletedChecklist { get; set; }
    public bool RequiresApproval { get; set; }
    public List<WorkflowAutomationDocument> Automations { get; set; } = [];
}
