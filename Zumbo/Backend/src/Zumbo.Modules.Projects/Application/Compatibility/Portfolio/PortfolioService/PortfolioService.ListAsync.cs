using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService{

    public async Task<PortfolioPageResponse> ListAsync(
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var visible = new List<PortfolioDocument>();
        string? cursor = null;
        do
        {
            var batch = await portfolios.ListByCursorAsync(
                item => item.OrganizationId == actor.OrganizationId
                    && (includeArchived || !item.Archived),
                cursor,
                100,
                ct);
            visible.AddRange(batch.Items.Where(item =>
                item.OwnerUserId == actor.UserId || item.ViewerUserIds.Contains(actor.UserId)));
            cursor = batch.NextCursor;
        } while (cursor is not null);

        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var ordered = visible
            .OrderByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();
        return new PortfolioPageResponse(
            ordered.Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .Select(item => ToResponse(item, actor.UserId))
                .ToList(),
            normalizedPage,
            normalizedPageSize,
            ordered.Count);
    }
}
