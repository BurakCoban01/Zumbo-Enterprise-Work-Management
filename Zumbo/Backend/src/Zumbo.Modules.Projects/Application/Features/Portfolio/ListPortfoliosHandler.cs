using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

public sealed class ListPortfoliosHandler(PortfolioService service)
{
    private ListPortfoliosSlice? slice;

    public ListPortfoliosHandler(
        IDocumentRepository<PortfolioDocument> portfolios,
        ICurrentUser currentUser)
        : this(null!) =>
        slice = new ListPortfoliosSlice(
            new PortfolioReadAccess(portfolios, currentUser),
            portfolios);

    public Task<PortfolioPageResponse> HandleAsync(
        ListPortfoliosQuery query,
        CancellationToken ct) =>
        slice?.HandleAsync(query, ct)
        ?? service.ListAsync(query.IncludeArchived, query.Page, query.PageSize, ct);
}
