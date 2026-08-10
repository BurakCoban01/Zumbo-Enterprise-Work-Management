using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;
using Zumbo.SharedKernel;

public sealed class WorkflowPolicyAdapter(WorkflowService workflows) : IWorkflowPolicy
{
    public async Task<WorkflowTransitionRule> EnsureTransitionAllowedAsync(
        string projectId,
        string issueType,
        string fromStatus,
        string toStatus,
        CancellationToken ct)
    {
        var workflow = await workflows.GetOrCreateDefaultAsync(projectId, ct);
        var scheme = workflow.IssueTypeSchemes?.SingleOrDefault(x =>
                x.IssueType.Equals(issueType, StringComparison.OrdinalIgnoreCase))
            ?? workflow.IssueTypeSchemes?.SingleOrDefault(x => x.IssueType == "*")
            ?? throw new ConflictException("WORKFLOW_ISSUE_SCHEME_NOT_FOUND", $"No workflow scheme exists for issue type '{issueType}'.");
        if (!scheme.Statuses.Contains(fromStatus, StringComparer.OrdinalIgnoreCase)
            || !scheme.Statuses.Contains(toStatus, StringComparer.OrdinalIgnoreCase))
        {
            throw new ConflictException("WORKFLOW_ISSUE_SCHEME_TRANSITION_FORBIDDEN", "The issue type scheme does not allow this status transition.");
        }
        var transition = workflow.Transitions.SingleOrDefault(x =>
            x.FromStatus.Equals(fromStatus, StringComparison.OrdinalIgnoreCase)
            && x.ToStatus.Equals(toStatus, StringComparison.OrdinalIgnoreCase));

        if (transition is null)
        {
            throw new ConflictException("WORKFLOW_TRANSITION_FORBIDDEN", $"Transition from {fromStatus} to {toStatus} is not allowed.");
        }

        return new WorkflowTransitionRule(
            transition.FromStatus,
            transition.ToStatus,
            transition.RequiresAssignee,
            transition.RequiresCompletedChecklist,
            transition.RequiresApproval,
            transition.Automations.Select(x => new WorkflowAutomationRule(x.Action, x.Value)).ToList(),
            workflow.Statuses.Single(x =>
                x.Name.Equals(transition.ToStatus, StringComparison.OrdinalIgnoreCase)).Category);
    }
}
