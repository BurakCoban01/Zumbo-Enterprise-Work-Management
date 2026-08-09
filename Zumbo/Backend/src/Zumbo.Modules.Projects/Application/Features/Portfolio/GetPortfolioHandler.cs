using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

public sealed class GetPortfolioHandler(PortfolioService service)
{
    private GetPortfolioSlice? slice;

    public GetPortfolioHandler(
        IDocumentRepository<PortfolioDocument> portfolios,
        ICurrentUser currentUser)
        : this(null!) =>
        slice = new GetPortfolioSlice(new PortfolioReadAccess(portfolios, currentUser));

    public Task<PortfolioResponse> HandleAsync(
        GetPortfolioQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.GetAsync(query.PortfolioId, query.IncludeArchived, ct);
}
