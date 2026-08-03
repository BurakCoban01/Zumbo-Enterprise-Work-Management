using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record WorkflowTransitionResponse(
    string FromStatus,
    string ToStatus,
    bool RequiresAssignee,
    bool RequiresCompletedChecklist,
    bool RequiresApproval,
    IReadOnlyCollection<WorkflowAutomationResponse> Automations);
