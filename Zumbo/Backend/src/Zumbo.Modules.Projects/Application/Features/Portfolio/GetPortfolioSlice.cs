namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

internal sealed class GetPortfolioSlice(PortfolioReadAccess access)
{
    internal async Task<PortfolioResponse> HandleAsync(
        GetPortfolioQuery query,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var portfolio = await access.GetDocumentAsync(
            query.PortfolioId,
            query.IncludeArchived,
            ct);
        PortfolioReadAccess.EnsureVisible(portfolio, actor.UserId);
        return PortfolioResponseMapper.ToResponse(portfolio, actor.UserId);
    }
}
