using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

internal sealed class ArchivePortfolioSlice(
    PortfolioReadAccess access,
    PortfolioMutationPersistence persistence,
    IPortfolioAuditWriter audit,
    IClock clock)
{
    internal async Task HandleAsync(ArchivePortfolioCommand command, CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var portfolio = await access.GetDocumentAsync(
            command.PortfolioId,
            includeArchived: false,
            ct);
        PortfolioReadAccess.EnsureOwner(portfolio, actor.UserId);
        portfolio.Archived = true;
        portfolio.UpdatedAt = clock.UtcNow;
        await persistence.ReplaceAsync(portfolio, ct);
        await audit.WriteAsync(
            "PortfolioArchived",
            portfolio.Id,
            "Active",
            "Archived",
            command.CorrelationId,
            ct);
    }
}
