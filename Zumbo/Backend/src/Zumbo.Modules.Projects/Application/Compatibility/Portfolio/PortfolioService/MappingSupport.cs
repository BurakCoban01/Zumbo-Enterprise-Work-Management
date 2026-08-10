namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService
{
    private static void Apply(
        PortfolioDocument portfolio,
        SavePortfolioRequest request,
        IReadOnlyCollection<string> viewers,
        DateTimeOffset now)
    {
        portfolio.Name = Required(request.Name, "Portfolio name", 120);
        portfolio.Description = Optional(request.Description, 1000);
        portfolio.ViewerUserIds = viewers.ToList();
        portfolio.UpdatedAt = now;
    }

    private static void Apply(InitiativeDocument initiative, SaveInitiativeRequest request)
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

    private static PortfolioResponse ToResponse(PortfolioDocument item, string userId) => new(
        item.Id,
        item.OwnerUserId,
        item.Name,
        item.Description,
        item.ViewerUserIds,
        item.Initiatives.Select(initiative => ToResponse(
            initiative,
            item.OwnerUserId == userId || initiative.OwnerUserId == userId)).ToList(),
        item.Dependencies.Select(ToResponse).ToList(),
        item.OwnerUserId == userId,
        item.Archived,
        item.UpdatedAt,
        item.Version);

    private static InitiativeResponse ToResponse(
        InitiativeDocument item,
        bool canUpdateStatus) => new(
        item.Id,
        item.Name,
        item.Summary,
        item.ParentInitiativeId,
        item.OwnerUserId,
        item.Status,
        item.Health,
        item.Confidence,
        item.TargetAt,
        item.ProjectIds,
        item.MilestoneLinks.Select(link =>
            new PortfolioMilestoneLinkResponse(link.ProjectId, link.MilestoneId)).ToList(),
        item.StatusUpdates.Select(update => new InitiativeStatusUpdateResponse(
            update.Id,
            update.Status,
            update.Health,
            update.Confidence,
            update.Note,
            update.AuthorUserId,
            update.CreatedAt)).ToList(),
        canUpdateStatus,
        ProjectHistoryRetentionPolicy.MaximumInitiativeStatusUpdates);

    private static PortfolioProjectDependencyResponse ToResponse(
        PortfolioProjectDependencyDocument item) => new(
        item.Id,
        item.SourceProjectId,
        item.TargetProjectId,
        item.Description,
        item.Status,
        item.RequiredBy);

    private static int Progress(int completed, int total) =>
        total <= 0 ? 0 : Math.Clamp((int)Math.Round(completed * 100d / total), 0, 100);
}
