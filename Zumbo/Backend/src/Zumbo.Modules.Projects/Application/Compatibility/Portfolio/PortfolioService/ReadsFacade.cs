using Zumbo.Modules.Projects.Application.Features.Portfolio;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService
{
    public async Task<PortfolioPageResponse> ListAsync(
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken ct)
        => await new ListPortfoliosHandler(portfolios, currentUser).HandleAsync(
            new ListPortfoliosQuery(includeArchived, page, pageSize), ct);

    public async Task<PortfolioResponse> GetAsync(
        string portfolioId,
        bool includeArchived,
        CancellationToken ct)
        => await new GetPortfolioHandler(portfolios, currentUser).HandleAsync(
            new GetPortfolioQuery(portfolioId, includeArchived), ct);

    public async Task<PortfolioRoadmapResponse> GetRoadmapAsync(
        string portfolioId,
        CancellationToken ct)
        => await new GetPortfolioRoadmapHandler(
            portfolios,
            directory,
            currentUser,
            clock).HandleAsync(new GetPortfolioRoadmapQuery(portfolioId), ct);
}
