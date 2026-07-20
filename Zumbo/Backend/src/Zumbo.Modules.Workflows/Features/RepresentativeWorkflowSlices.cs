using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record CreateWorkflowRequest(
    string ProjectId,
    IReadOnlyCollection<WorkflowTransitionRequest> Transitions,
    IReadOnlyCollection<WorkflowStatusRequest>? Statuses = null,
    IReadOnlyCollection<WorkflowIssueTypeSchemeRequest>? IssueTypeSchemes = null);

public sealed record WorkflowStatusRequest(string Name, string Category);

public sealed record WorkflowTransitionRequest(
    string FromStatus,
    string ToStatus,
    bool RequiresAssignee,
    bool RequiresCompletedChecklist,
    bool RequiresApproval = false,
    IReadOnlyCollection<WorkflowAutomationRequest>? Automations = null);

public sealed record WorkflowAutomationRequest(string Action, string? Value = null);

public sealed record WorkflowIssueTypeSchemeRequest(
    string IssueType,
    string DefaultStatus,
    IReadOnlyCollection<string> Statuses,
    IReadOnlyCollection<string>? DoneStatuses = null);

public sealed record WorkflowResponse(
    string Id,
    string ProjectId,
    IReadOnlyCollection<WorkflowStatusResponse> Statuses,
    IReadOnlyCollection<WorkflowTransitionResponse> Transitions,
    long Version = 0,
    int PublishedVersion = 1,
    IReadOnlyCollection<WorkflowIssueTypeSchemeResponse>? IssueTypeSchemes = null,
    bool HasDraft = false) : IVersionedResource;

public sealed record WorkflowStatusResponse(string Name, string Category);

public sealed record WorkflowTransitionResponse(
    string FromStatus,
    string ToStatus,
    bool RequiresAssignee,
    bool RequiresCompletedChecklist,
    bool RequiresApproval,
    IReadOnlyCollection<WorkflowAutomationResponse> Automations);

public sealed record WorkflowAutomationResponse(string Action, string? Value);

public sealed record WorkflowIssueTypeSchemeResponse(
    string IssueType,
    string DefaultStatus,
    IReadOnlyCollection<string> Statuses,
    IReadOnlyCollection<string> DoneStatuses);

public sealed record WorkflowVersionResponse(
    int Number,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    int StatusCount,
    int TransitionCount);

public sealed class UpsertWorkflowValidator
{
    public static void Validate(CreateWorkflowRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId) || request.Transitions.Count == 0)
        {
            throw new ValidationException("Project id and transitions are required.");
        }
    }
}

public sealed class UpsertWorkflowHandler(WorkflowService service)
{
    public Task<WorkflowResponse> HandleAsync(CreateWorkflowRequest request, string correlationId, CancellationToken ct) =>
        service.UpsertAsync(request, correlationId, ct);
}

public sealed class SaveWorkflowDraftHandler(WorkflowService service)
{
    public Task<WorkflowResponse> HandleAsync(CreateWorkflowRequest request, string correlationId, CancellationToken ct) =>
        service.SaveDraftAsync(request, correlationId, ct);
}

public sealed class PublishWorkflowHandler(WorkflowService service)
{
    public Task<WorkflowResponse> HandleAsync(string projectId, string correlationId, CancellationToken ct) =>
        service.PublishAsync(projectId, correlationId, ct);
}

public sealed record GetWorkflowQuery(string ProjectId);

public sealed class GetWorkflowValidator
{
    public static void Validate(GetWorkflowQuery query) => ArgumentNullException.ThrowIfNull(query);
}

public sealed class GetWorkflowHandler(WorkflowService service)
{
    public Task<WorkflowResponse> HandleAsync(GetWorkflowQuery query, CancellationToken ct)
    {
        GetWorkflowValidator.Validate(query);
        return service.GetOrCreateDefaultAsync(query.ProjectId, ct);
    }
}
