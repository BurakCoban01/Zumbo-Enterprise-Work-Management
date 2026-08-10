using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService
{
    private (string UserId, string OrganizationId) CurrentActor() => (
        currentUser.UserId ?? throw new UnauthorizedException("Authenticated user is required."),
        currentUser.OrganizationId ?? throw new UnauthorizedException("Active organization is required."));

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

    private static void EnsureVisible(PortfolioDocument portfolio, string userId)
    {
        if (portfolio.OwnerUserId != userId
            && !portfolio.ViewerUserIds.Contains(userId, StringComparer.Ordinal))
        {
            throw new NotFoundException("PORTFOLIO_NOT_FOUND", "Portfolio was not found.");
        }
    }

    private static void EnsureOwner(PortfolioDocument portfolio, string userId)
    {
        EnsureVisible(portfolio, userId);
        if (portfolio.OwnerUserId != userId)
            throw new ForbiddenException("Only the portfolio owner can change this portfolio.");
    }

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
