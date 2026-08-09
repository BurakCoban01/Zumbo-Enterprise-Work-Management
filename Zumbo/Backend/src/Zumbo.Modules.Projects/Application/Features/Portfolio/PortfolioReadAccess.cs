using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

internal sealed class PortfolioReadAccess(
    IDocumentRepository<PortfolioDocument> portfolios,
    ICurrentUser currentUser)
{
    internal (string UserId, string OrganizationId) CurrentActor() => (
        currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required."),
        currentUser.OrganizationId
            ?? throw new UnauthorizedException("Active organization is required."));

    internal async Task<PortfolioDocument> GetDocumentAsync(
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

    internal static bool CanView(PortfolioDocument portfolio, string userId) =>
        portfolio.OwnerUserId == userId
        || portfolio.ViewerUserIds.Contains(userId, StringComparer.Ordinal);

    internal static void EnsureVisible(PortfolioDocument portfolio, string userId)
    {
        if (!CanView(portfolio, userId))
            throw new NotFoundException("PORTFOLIO_NOT_FOUND", "Portfolio was not found.");
    }

    internal static void EnsureOwner(PortfolioDocument portfolio, string userId)
    {
        EnsureVisible(portfolio, userId);
        if (portfolio.OwnerUserId != userId)
            throw new ForbiddenException("Only the portfolio owner can change this portfolio.");
    }
}
