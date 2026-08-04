using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService{

    public async Task<PortfolioRoadmapResponse> GetRoadmapAsync(
        string portfolioId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var portfolio = await GetDocumentAsync(portfolioId, includeArchived: false, ct);
        EnsureVisible(portfolio, actor.UserId);
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
                        Progress(project.CompletedWorkItems, project.TotalWorkItems),
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
                Progress(completed, total),
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
            portfolio.Dependencies.Select(ToResponse).ToList());
    }
}
