using Zumbo.Modules.Projects.Application.Features.Portfolio;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService
{
    public async Task<PortfolioResponse> SaveAsync(
        string? portfolioId,
        SavePortfolioRequest request,
        string correlationId,
        CancellationToken ct)
        => await new SavePortfolioHandler(
            portfolios,
            directory,
            audit,
            currentUser,
            clock,
            expectedVersion).HandleAsync(
                new SavePortfolioCommand(portfolioId, request, correlationId), ct);

    public async Task ArchiveAsync(
        string portfolioId,
        string correlationId,
        CancellationToken ct)
        => await new ArchivePortfolioHandler(
            portfolios,
            audit,
            currentUser,
            clock,
            expectedVersion).HandleAsync(
                new ArchivePortfolioCommand(portfolioId, correlationId), ct);
}
