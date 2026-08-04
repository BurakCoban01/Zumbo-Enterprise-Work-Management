namespace Zumbo.Modules.Workflows.Application.Features.RunQueries;

public sealed record ListAutomationRunsQuery(
    string ProjectId,
    string? RuleId,
    string? Status,
    int Page,
    int PageSize);
