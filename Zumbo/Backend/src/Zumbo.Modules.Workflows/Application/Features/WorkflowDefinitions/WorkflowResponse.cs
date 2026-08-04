using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record WorkflowResponse(
    string Id,
    string ProjectId,
    IReadOnlyCollection<WorkflowStatusResponse> Statuses,
    IReadOnlyCollection<WorkflowTransitionResponse> Transitions,
    long Version = 0,
    int PublishedVersion = 1,
    IReadOnlyCollection<WorkflowIssueTypeSchemeResponse>? IssueTypeSchemes = null,
    bool HasDraft = false,
    int PublishedVersionRetentionLimit = WorkflowRetentionPolicy.MaximumPublishedVersions,
    int RetainedPublishedVersionCount = 0,
    int? OldestRetainedPublishedVersion = null) : IVersionedResource;
