using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record WorkflowTransitionRequest(
    string FromStatus,
    string ToStatus,
    bool RequiresAssignee,
    bool RequiresCompletedChecklist,
    bool RequiresApproval = false,
    IReadOnlyCollection<WorkflowAutomationRequest>? Automations = null);
