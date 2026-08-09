using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

public sealed class GetPortfolioRoadmapHandler(PortfolioService service)
{
    private GetPortfolioRoadmapSlice? slice;

    public GetPortfolioRoadmapHandler(
        IDocumentRepository<PortfolioDocument> portfolios,
        IPortfolioDirectory directory,
        ICurrentUser currentUser,
        IClock clock)
        : this(null!) =>
        slice = new GetPortfolioRoadmapSlice(
            new PortfolioReadAccess(portfolios, currentUser),
            directory,
            clock);

    public Task<PortfolioRoadmapResponse> HandleAsync(
        GetPortfolioRoadmapQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.GetRoadmapAsync(query.PortfolioId, ct);
}
