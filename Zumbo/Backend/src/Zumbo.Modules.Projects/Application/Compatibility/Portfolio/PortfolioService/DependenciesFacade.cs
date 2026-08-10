using Zumbo.Modules.Projects.Application.Features.Portfolio;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService
{
    public async Task<PortfolioResponse> SaveDependencyAsync(
        string portfolioId,
        string? dependencyId,
        SavePortfolioDependencyRequest request,
        string correlationId,
        CancellationToken ct)
        => await new SavePortfolioDependencyHandler(
            portfolios,
            directory,
            audit,
            currentUser,
            clock,
            expectedVersion).HandleAsync(
                new SavePortfolioDependencyCommand(
                    portfolioId,
                    dependencyId,
                    request,
                    correlationId),
                ct);
}
