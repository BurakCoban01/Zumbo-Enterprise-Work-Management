using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService{

    private static void EnsureOwner(PortfolioDocument portfolio, string userId)
    {
        EnsureVisible(portfolio, userId);
        if (portfolio.OwnerUserId != userId)
            throw new ForbiddenException("Only the portfolio owner can change this portfolio.");
    }
}
