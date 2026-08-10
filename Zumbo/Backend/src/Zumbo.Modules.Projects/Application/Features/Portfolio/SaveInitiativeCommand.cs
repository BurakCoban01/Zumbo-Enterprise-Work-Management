namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

public sealed record SaveInitiativeCommand(
    string PortfolioId,
    string? InitiativeId,
    SaveInitiativeRequest Request,
    string CorrelationId);
