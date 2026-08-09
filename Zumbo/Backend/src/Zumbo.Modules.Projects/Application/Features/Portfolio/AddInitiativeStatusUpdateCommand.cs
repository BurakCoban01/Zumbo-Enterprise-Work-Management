namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

public sealed record AddInitiativeStatusUpdateCommand(
    string PortfolioId,
    string InitiativeId,
    AddInitiativeStatusUpdateRequest Request,
    string CorrelationId);
