using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService{

    public async Task<PortfolioResponse> SaveAsync(
        string? portfolioId,
        SavePortfolioRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var viewers = NormalizeIds(request.ViewerUserIds, 50, "Portfolio viewer");
        viewers.Remove(actor.UserId);
        await directory.EnsureOrganizationUsersAsync(
            actor.OrganizationId,
            viewers.Append(actor.UserId).ToList(),
            ct);
        var now = clock.UtcNow;
        PortfolioDocument portfolio;
        if (string.IsNullOrWhiteSpace(portfolioId))
        {
            portfolio = new PortfolioDocument
            {
                OrganizationId = actor.OrganizationId,
                OwnerUserId = actor.UserId,
                CreatedAt = now
            };
            Apply(portfolio, request, viewers, now);
            portfolio = await portfolios.CreateAsync(portfolio, ct);
            await audit.WriteAsync(
                "PortfolioCreated", portfolio.Id, null, portfolio.Name, correlationId, ct);
        }
        else
        {
            portfolio = await GetDocumentAsync(portfolioId, includeArchived: false, ct);
            EnsureOwner(portfolio, actor.UserId);
            var oldValue = portfolio.Name;
            Apply(portfolio, request, viewers, now);
            await ReplaceAsync(portfolio, ct);
            await audit.WriteAsync(
                "PortfolioUpdated", portfolio.Id, oldValue, portfolio.Name, correlationId, ct);
        }
        return ToResponse(portfolio, actor.UserId);
    }
}
