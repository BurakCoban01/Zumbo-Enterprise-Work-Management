namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

internal static class PortfolioMutationMapper
{
    internal static void Apply(
        PortfolioDocument portfolio,
        SavePortfolioRequest request,
        IReadOnlyCollection<string> viewers,
        DateTimeOffset now)
    {
        portfolio.Name = PortfolioValidation.Required(request.Name, "Portfolio name", 120);
        portfolio.Description = PortfolioValidation.Optional(request.Description, 1000);
        portfolio.ViewerUserIds = viewers.ToList();
        portfolio.UpdatedAt = now;
    }

    internal static void Apply(InitiativeDocument initiative, SaveInitiativeRequest request)
    {
        initiative.Name = request.Name;
        initiative.Summary = request.Summary;
        initiative.ParentInitiativeId = request.ParentInitiativeId;
        initiative.OwnerUserId = request.OwnerUserId;
        initiative.Status = request.Status;
        initiative.Health = request.Health;
        initiative.Confidence = request.Confidence;
        initiative.TargetAt = request.TargetAt;
        initiative.ProjectIds = request.ProjectIds.ToList();
        initiative.MilestoneLinks = request.MilestoneLinks.Select(link =>
            new PortfolioMilestoneLinkDocument
            {
                ProjectId = link.ProjectId,
                MilestoneId = link.MilestoneId
            }).ToList();
    }
}
