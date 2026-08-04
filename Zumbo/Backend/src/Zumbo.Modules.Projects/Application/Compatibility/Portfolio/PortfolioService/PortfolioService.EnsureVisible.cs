using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService{

    private static void EnsureVisible(PortfolioDocument portfolio, string userId)
    {
        if (portfolio.OwnerUserId != userId
            && !portfolio.ViewerUserIds.Contains(userId, StringComparer.Ordinal))
        {
            throw new NotFoundException("PORTFOLIO_NOT_FOUND", "Portfolio was not found.");
        }
    }
}
