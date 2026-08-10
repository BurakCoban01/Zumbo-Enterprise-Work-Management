namespace Zumbo.Modules.WorkItems.Application.Features.Schema;

public sealed record ValidateWorkItemSearchFilterQuery(
    string ProjectId,
    string? IssueTypeKey,
    string? CustomFieldKey,
    string? CustomFieldValue);
