using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

internal sealed class SavePortfolioSlice(
    PortfolioReadAccess access,
    PortfolioMutationPersistence persistence,
    IDocumentRepository<PortfolioDocument> portfolios,
    IPortfolioDirectory directory,
    IPortfolioAuditWriter audit,
    IClock clock)
{
    internal async Task<PortfolioResponse> HandleAsync(
        SavePortfolioCommand command,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var viewers = PortfolioValidation.NormalizeIds(
            command.Request.ViewerUserIds,
            50,
            "Portfolio viewer");
        viewers.Remove(actor.UserId);
        await directory.EnsureOrganizationUsersAsync(
            actor.OrganizationId,
            viewers.Append(actor.UserId).ToList(),
            ct);
        var now = clock.UtcNow;
        PortfolioDocument portfolio;
        if (string.IsNullOrWhiteSpace(command.PortfolioId))
        {
            portfolio = new PortfolioDocument
            {
                OrganizationId = actor.OrganizationId,
                OwnerUserId = actor.UserId,
                CreatedAt = now
            };
            PortfolioMutationMapper.Apply(portfolio, command.Request, viewers, now);
            portfolio = await portfolios.CreateAsync(portfolio, ct);
            await audit.WriteAsync(
                "PortfolioCreated",
                portfolio.Id,
                null,
                portfolio.Name,
                command.CorrelationId,
                ct);
        }
        else
        {
            portfolio = await access.GetDocumentAsync(
                command.PortfolioId,
                includeArchived: false,
                ct);
            PortfolioReadAccess.EnsureOwner(portfolio, actor.UserId);
            var oldValue = portfolio.Name;
            PortfolioMutationMapper.Apply(portfolio, command.Request, viewers, now);
            await persistence.ReplaceAsync(portfolio, ct);
            await audit.WriteAsync(
                "PortfolioUpdated",
                portfolio.Id,
                oldValue,
                portfolio.Name,
                command.CorrelationId,
                ct);
        }
        return PortfolioResponseMapper.ToResponse(portfolio, actor.UserId);
    }
}
