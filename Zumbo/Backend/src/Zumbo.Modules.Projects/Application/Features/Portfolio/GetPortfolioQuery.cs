namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

public sealed record GetPortfolioQuery(string PortfolioId, bool IncludeArchived);
