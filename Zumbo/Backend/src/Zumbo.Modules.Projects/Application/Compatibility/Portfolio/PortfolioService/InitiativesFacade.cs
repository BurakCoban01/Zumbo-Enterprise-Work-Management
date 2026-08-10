using Zumbo.Modules.Projects.Application.Features.Portfolio;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService
{
    public async Task<PortfolioResponse> SaveInitiativeAsync(
        string portfolioId,
        string? initiativeId,
        SaveInitiativeRequest request,
        string correlationId,
        CancellationToken ct)
        => await new SaveInitiativeHandler(
            portfolios,
            directory,
            audit,
            currentUser,
            clock,
            expectedVersion).HandleAsync(
                new SaveInitiativeCommand(
                    portfolioId,
                    initiativeId,
                    request,
                    correlationId),
                ct);

    public async Task<PortfolioResponse> AddStatusUpdateAsync(
        string portfolioId,
        string initiativeId,
        AddInitiativeStatusUpdateRequest request,
        string correlationId,
        CancellationToken ct)
        => await new AddInitiativeStatusUpdateHandler(
            portfolios,
            audit,
            currentUser,
            clock,
            expectedVersion).HandleAsync(
                new AddInitiativeStatusUpdateCommand(
                    portfolioId,
                    initiativeId,
                    request,
                    correlationId),
                ct);
}
