using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record CreateWorkflowRequest(
    string ProjectId,
    IReadOnlyCollection<WorkflowTransitionRequest> Transitions,
    IReadOnlyCollection<WorkflowStatusRequest>? Statuses = null,
    IReadOnlyCollection<WorkflowIssueTypeSchemeRequest>? IssueTypeSchemes = null);
