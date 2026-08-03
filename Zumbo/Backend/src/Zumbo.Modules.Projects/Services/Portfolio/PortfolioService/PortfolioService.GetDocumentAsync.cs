using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService{

    private async Task<PortfolioDocument> GetDocumentAsync(
        string portfolioId,
        bool includeArchived,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        return await portfolios.SelectAsync(
            item => item.Id == portfolioId
                && item.OrganizationId == actor.OrganizationId
                && (includeArchived || !item.Archived),
            ct)
            ?? throw new NotFoundException("PORTFOLIO_NOT_FOUND", "Portfolio was not found.");
    }
}
