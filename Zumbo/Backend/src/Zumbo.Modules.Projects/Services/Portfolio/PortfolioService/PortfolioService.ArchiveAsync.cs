using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService{

    public async Task ArchiveAsync(
        string portfolioId,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var portfolio = await GetDocumentAsync(portfolioId, includeArchived: false, ct);
        EnsureOwner(portfolio, actor.UserId);
        portfolio.Archived = true;
        portfolio.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(portfolio, ct);
        await audit.WriteAsync(
            "PortfolioArchived", portfolio.Id, "Active", "Archived", correlationId, ct);
    }
}
