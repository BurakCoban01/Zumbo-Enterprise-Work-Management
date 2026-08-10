namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

public sealed record SavePortfolioCommand(
    string? PortfolioId,
    SavePortfolioRequest Request,
    string CorrelationId);
