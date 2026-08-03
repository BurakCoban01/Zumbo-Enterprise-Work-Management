using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemSearchRequest(
    string? ProjectId,
    string? AssigneeUserId,
    string? Status,
    string? Text,
    int Page = 1,
    int PageSize = 100,
    bool Archived = false,
    string? IssueType = null,
    string? CustomFieldKey = null,
    string? CustomFieldValue = null);
