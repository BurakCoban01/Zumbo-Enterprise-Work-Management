namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

internal static class PortfolioResponseMapper
{
    internal static PortfolioResponse ToResponse(PortfolioDocument item, string userId) => new(
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

    internal static InitiativeResponse ToResponse(
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

    internal static PortfolioProjectDependencyResponse ToResponse(
        PortfolioProjectDependencyDocument item) => new(
        item.Id,
        item.SourceProjectId,
        item.TargetProjectId,
        item.Description,
        item.Status,
        item.RequiredBy);

    internal static int Progress(int completed, int total) =>
        total <= 0 ? 0 : Math.Clamp((int)Math.Round(completed * 100d / total), 0, 100);
}
