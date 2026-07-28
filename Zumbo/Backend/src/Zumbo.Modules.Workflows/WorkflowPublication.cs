using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record WorkflowPublicationCandidate(
    string ProjectId,
    int Version,
    IReadOnlyCollection<WorkflowStatusResponse> Statuses,
    IReadOnlyCollection<WorkflowTransitionResponse> Transitions,
    IReadOnlyCollection<WorkflowIssueTypeSchemeResponse> IssueTypeSchemes);

public interface IWorkflowPublicationGuard
{
    Task ValidateAsync(WorkflowPublicationCandidate candidate, CancellationToken ct);
}

internal static class WorkflowDocumentMapper
{
    public static WorkflowVersionDocument ToVersion(
        WorkflowDefinitionAggregate definition,
        IReadOnlyCollection<WorkflowIssueTypeSchemeRequest> schemes,
        int number) =>
        new()
        {
            Number = number,
            State = "Draft",
            CreatedAt = definition.DefinedAt,
            Statuses = definition.Statuses.Select(x => new WorkflowStatusDocument
            {
                Name = x.Name,
                Category = x.Category
            }).ToList(),
            Transitions = definition.Transitions.Select(x => new WorkflowTransitionDocument
            {
                FromStatus = x.FromStatus,
                ToStatus = x.ToStatus,
                RequiresAssignee = x.RequiresAssignee,
                RequiresCompletedChecklist = x.RequiresCompletedChecklist,
                RequiresApproval = x.RequiresApproval,
                Automations = (x.Automations ?? []).Select(automation => new WorkflowAutomationDocument
                {
                    Action = automation.Action,
                    Value = automation.Value
                }).ToList()
            }).ToList(),
            IssueTypeSchemes = schemes.Select(x => new WorkflowIssueTypeSchemeDocument
            {
                IssueType = x.IssueType,
                DefaultStatus = x.DefaultStatus,
                Statuses = x.Statuses.ToList(),
                DoneStatuses = (x.DoneStatuses ?? []).ToList()
            }).ToList()
        };

    public static WorkflowVersionDocument CopyPublished(WorkflowVersionDocument draft, DateTimeOffset publishedAt) =>
        new()
        {
            Number = draft.Number,
            State = "Published",
            CreatedAt = draft.CreatedAt,
            PublishedAt = publishedAt,
            Statuses = draft.Statuses.Select(Copy).ToList(),
            Transitions = draft.Transitions.Select(Copy).ToList(),
            IssueTypeSchemes = draft.IssueTypeSchemes.Select(Copy).ToList()
        };

    public static WorkflowPublicationCandidate ToCandidate(string projectId, WorkflowVersionDocument draft) =>
        new(
            projectId,
            draft.Number,
            draft.Statuses.Select(x => new WorkflowStatusResponse(x.Name, x.Category)).ToList(),
            draft.Transitions.Select(ToResponse).ToList(),
            draft.IssueTypeSchemes.Select(ToResponse).ToList());

    public static WorkflowResponse ToResponse(WorkflowDefinitionDocument workflow) =>
        new(
            workflow.Id,
            workflow.ProjectId,
            workflow.Statuses.Select(x => new WorkflowStatusResponse(x.Name, x.Category)).ToList(),
            workflow.Transitions.Select(ToResponse).ToList(),
            workflow.Version,
            Math.Max(workflow.PublishedVersion, 1),
            EnsureSchemes(workflow).Select(ToResponse).ToList(),
            workflow.Draft is not null,
            WorkflowRetentionPolicy.MaximumPublishedVersions,
            workflow.PublishedVersions.Count,
            workflow.PublishedVersions.Count == 0
                ? null
                : workflow.PublishedVersions.Min(version => version.Number));

    public static WorkflowResponse ToDraftResponse(WorkflowDefinitionDocument workflow)
    {
        var draft = workflow.Draft
            ?? throw new NotFoundException("WORKFLOW_DRAFT_NOT_FOUND", "Workflow draft was not found.");
        return new WorkflowResponse(
            workflow.Id,
            workflow.ProjectId,
            draft.Statuses.Select(x => new WorkflowStatusResponse(x.Name, x.Category)).ToList(),
            draft.Transitions.Select(ToResponse).ToList(),
            workflow.Version,
            draft.Number,
            draft.IssueTypeSchemes.Select(ToResponse).ToList(),
            true,
            WorkflowRetentionPolicy.MaximumPublishedVersions,
            workflow.PublishedVersions.Count,
            workflow.PublishedVersions.Count == 0
                ? null
                : workflow.PublishedVersions.Min(version => version.Number));
    }

    public static IReadOnlyCollection<WorkflowIssueTypeSchemeDocument> EnsureSchemes(WorkflowDefinitionDocument workflow)
    {
        if (workflow.IssueTypeSchemes.Count > 0)
        {
            return workflow.IssueTypeSchemes;
        }

        var todo = workflow.Statuses.First(x => x.Category == "Todo").Name;
        return
        [
            new WorkflowIssueTypeSchemeDocument
            {
                IssueType = "*",
                DefaultStatus = todo,
                Statuses = workflow.Statuses.Select(x => x.Name).ToList(),
                DoneStatuses = workflow.Statuses.Where(x => x.Category == "Done").Select(x => x.Name).ToList()
            }
        ];
    }

    private static WorkflowTransitionResponse ToResponse(WorkflowTransitionDocument transition) =>
        new(
            transition.FromStatus,
            transition.ToStatus,
            transition.RequiresAssignee,
            transition.RequiresCompletedChecklist,
            transition.RequiresApproval,
            (transition.Automations ?? []).Select(x => new WorkflowAutomationResponse(x.Action, x.Value)).ToList());

    private static WorkflowIssueTypeSchemeResponse ToResponse(WorkflowIssueTypeSchemeDocument scheme) =>
        new(scheme.IssueType, scheme.DefaultStatus, scheme.Statuses, scheme.DoneStatuses);

    private static WorkflowStatusDocument Copy(WorkflowStatusDocument status) =>
        new() { Name = status.Name, Category = status.Category };

    private static WorkflowTransitionDocument Copy(WorkflowTransitionDocument transition) =>
        new()
        {
            FromStatus = transition.FromStatus,
            ToStatus = transition.ToStatus,
            RequiresAssignee = transition.RequiresAssignee,
            RequiresCompletedChecklist = transition.RequiresCompletedChecklist,
            RequiresApproval = transition.RequiresApproval,
            Automations = transition.Automations.Select(x => new WorkflowAutomationDocument
            {
                Action = x.Action,
                Value = x.Value
            }).ToList()
        };

    private static WorkflowIssueTypeSchemeDocument Copy(WorkflowIssueTypeSchemeDocument scheme) =>
        new()
        {
            IssueType = scheme.IssueType,
            DefaultStatus = scheme.DefaultStatus,
            Statuses = [.. scheme.Statuses],
            DoneStatuses = [.. scheme.DoneStatuses]
        };
}
