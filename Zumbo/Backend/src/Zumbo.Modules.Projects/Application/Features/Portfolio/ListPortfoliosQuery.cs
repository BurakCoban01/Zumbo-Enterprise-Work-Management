namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

public sealed record ListPortfoliosQuery(
    bool IncludeArchived,
    int Page,
    int PageSize);
