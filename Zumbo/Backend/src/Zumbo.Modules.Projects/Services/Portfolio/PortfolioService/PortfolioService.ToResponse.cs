using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService{

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
}
