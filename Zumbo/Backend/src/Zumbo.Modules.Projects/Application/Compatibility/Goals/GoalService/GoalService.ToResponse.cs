using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService{

    private static GoalResponse ToResponse(GoalDocument item, string userId) => new(
        item.Id,
        item.OwnerUserId,
        item.Name,
        item.Description,
        DateOnly.FromDateTime(item.PeriodStartAtUtc.UtcDateTime),
        DateOnly.FromDateTime(item.PeriodEndAtUtc.UtcDateTime),
        item.Status,
        item.Health,
        item.Confidence,
        Progress(item),
        item.ViewerUserIds,
        item.InitiativeLinks.Select(link =>
            new GoalInitiativeLinkResponse(link.PortfolioId, link.InitiativeId)).ToList(),
        item.ProjectIds,
        item.KeyResults.Select(keyResult => ToResponse(
            keyResult,
            item.OwnerUserId == userId || keyResult.OwnerUserId == userId)).ToList(),
        item.StatusUpdates.Select(update => new GoalStatusUpdateResponse(
            update.Id,
            update.Status,
            update.Health,
            update.Confidence,
            update.Note,
            update.AuthorUserId,
            update.CreatedAt)).ToList(),
        item.OwnerUserId == userId,
        item.OwnerUserId == userId,
        item.Archived,
        item.UpdatedAt,
        item.Version,
        ProjectHistoryRetentionPolicy.MaximumGoalStatusUpdates);

    private static KeyResultResponse ToResponse(
        KeyResultDocument item,
        bool canUpdate) => new(
        item.Id,
        item.OwnerUserId,
        item.Name,
        item.Description,
        item.BaselineValue,
        item.TargetValue,
        item.CurrentValue,
        item.Unit,
        item.Direction,
        Progress(item),
        item.Confidence,
        item.ProgressUpdates.Select(update => new KeyResultProgressUpdateResponse(
            update.Id,
            update.PreviousValue,
            update.CurrentValue,
            update.Confidence,
            update.Note,
            update.AuthorUserId,
            update.CreatedAt)).ToList(),
        canUpdate,
        ProjectHistoryRetentionPolicy.MaximumKeyResultProgressUpdates);
}
