using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

internal sealed class GetPortfolioRoadmapSlice(
    PortfolioReadAccess access,
    IPortfolioDirectory directory,
    IClock clock)
{
    internal async Task<PortfolioRoadmapResponse> HandleAsync(
        GetPortfolioRoadmapQuery query,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var portfolio = await access.GetDocumentAsync(
            query.PortfolioId,
            includeArchived: false,
            ct);
        PortfolioReadAccess.EnsureVisible(portfolio, actor.UserId);
        var projectIds = portfolio.Initiatives
            .SelectMany(item => item.ProjectIds)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var source = await directory.ReadProjectSourcesAsync(
            portfolio.OrganizationId,
            projectIds,
            ct);
        var byId = source.Projects.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var initiatives = portfolio.Initiatives.Select(initiative =>
        {
            var projects = initiative.ProjectIds
                .Where(byId.ContainsKey)
                .Select(projectId =>
                {
                    var project = byId[projectId];
                    return new PortfolioRoadmapProjectResponse(
                        project.Id,
                        project.Key,
                        project.Name,
                        project.TotalWorkItems,
                        project.CompletedWorkItems,
                        project.OverdueWorkItems,
                        PortfolioResponseMapper.Progress(
                            project.CompletedWorkItems,
                            project.TotalWorkItems),
                        project.Milestones,
                        project.UpdatedAt);
                })
                .ToList();
            var total = projects.Sum(item => item.TotalWorkItems);
            var completed = projects.Sum(item => item.CompletedWorkItems);
            return new PortfolioRoadmapInitiativeResponse(
                initiative.Id,
                initiative.Name,
                initiative.ParentInitiativeId,
                initiative.OwnerUserId,
                initiative.Status,
                initiative.Health,
                initiative.Confidence,
                initiative.TargetAt,
                total,
                completed,
                projects.Sum(item => item.OverdueWorkItems),
                PortfolioResponseMapper.Progress(completed, total),
                projects);
        }).ToList();
        return new PortfolioRoadmapResponse(
            portfolio.Id,
            source.UnavailableProjectIds.Count == 0
                ? PortfolioSourceStatuses.Ready
                : PortfolioSourceStatuses.Partial,
            clock.UtcNow,
            source.UnavailableProjectIds,
            initiatives,
            portfolio.Dependencies.Select(PortfolioResponseMapper.ToResponse).ToList());
    }
}
