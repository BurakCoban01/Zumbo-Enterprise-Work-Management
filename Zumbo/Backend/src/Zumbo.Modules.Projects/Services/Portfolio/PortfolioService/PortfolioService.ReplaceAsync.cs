using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService{

    private async Task ReplaceAsync(PortfolioDocument portfolio, CancellationToken ct)
    {
        var result = await portfolios.ReplaceByVersionAsync(
            item => item.Id == portfolio.Id && item.OrganizationId == portfolio.OrganizationId,
            portfolio,
            expectedVersion.Consume(portfolio.Version),
            ct);
        if (!result.Found)
            throw new NotFoundException("PORTFOLIO_NOT_FOUND", "Portfolio was not found.");
        portfolio.Version = result.Version!.Value;
    }
}
