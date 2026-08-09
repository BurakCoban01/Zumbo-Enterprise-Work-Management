namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

public sealed record SavePortfolioDependencyCommand(
    string PortfolioId,
    string? DependencyId,
    SavePortfolioDependencyRequest Request,
    string CorrelationId);
