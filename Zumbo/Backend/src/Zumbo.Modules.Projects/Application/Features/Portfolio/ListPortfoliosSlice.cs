using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

internal sealed class ListPortfoliosSlice(
    PortfolioReadAccess access,
    IDocumentRepository<PortfolioDocument> portfolios)
{
    internal async Task<PortfolioPageResponse> HandleAsync(
        ListPortfoliosQuery query,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var visible = new List<PortfolioDocument>();
        string? cursor = null;
        do
        {
            var batch = await portfolios.ListByCursorAsync(
                item => item.OrganizationId == actor.OrganizationId
                    && (query.IncludeArchived || !item.Archived),
                cursor,
                100,
                ct);
            visible.AddRange(batch.Items.Where(item =>
                PortfolioReadAccess.CanView(item, actor.UserId)));
            cursor = batch.NextCursor;
        } while (cursor is not null);

        var normalizedPage = Math.Max(query.Page, 1);
        var normalizedPageSize = Math.Clamp(query.PageSize, 1, 100);
        var ordered = visible
            .OrderByDescending(item => item.UpdatedAt)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();
        return new PortfolioPageResponse(
            ordered.Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .Select(item => PortfolioResponseMapper.ToResponse(item, actor.UserId))
                .ToList(),
            normalizedPage,
            normalizedPageSize,
            ordered.Count);
    }
}
