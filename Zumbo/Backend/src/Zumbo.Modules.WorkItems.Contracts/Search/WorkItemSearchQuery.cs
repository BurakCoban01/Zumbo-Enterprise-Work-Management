namespace Zumbo.BuildingBlocks.Application.Search;

public sealed record WorkItemSearchQuery(
    string OrganizationId,
    string ProjectId,
    string? Text,
    string? AssigneeUserId,
    string? Status,
    int Page = 1,
    int PageSize = 100,
    string? IssueType = null,
    string? CustomFieldKey = null,
    string? CustomFieldValue = null);
