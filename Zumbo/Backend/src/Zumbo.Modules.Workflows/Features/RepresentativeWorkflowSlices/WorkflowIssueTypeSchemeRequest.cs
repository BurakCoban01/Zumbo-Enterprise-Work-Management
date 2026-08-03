using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record WorkflowIssueTypeSchemeRequest(
    string IssueType,
    string DefaultStatus,
    IReadOnlyCollection<string> Statuses,
    IReadOnlyCollection<string>? DoneStatuses = null);
