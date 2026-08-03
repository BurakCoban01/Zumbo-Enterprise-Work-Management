using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record WorkflowPublicationCandidate(
    string ProjectId,
    int Version,
    IReadOnlyCollection<WorkflowStatusResponse> Statuses,
    IReadOnlyCollection<WorkflowTransitionResponse> Transitions,
    IReadOnlyCollection<WorkflowIssueTypeSchemeResponse> IssueTypeSchemes);
