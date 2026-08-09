namespace Zumbo.Modules.Projects.Application.Features.Goals;

internal static class GoalResponseMapper
{
    internal static GoalResponse ToResponse(GoalDocument item, string userId) => new(
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

    internal static int Progress(GoalDocument goal)
    {
        if (goal.KeyResults.Count == 0) return 0;
        return (int)Math.Round(goal.KeyResults.Average(Progress));
    }

    private static KeyResultResponse ToResponse(KeyResultDocument item, bool canUpdate) => new(
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

    private static int Progress(KeyResultDocument keyResult)
    {
        var distance = keyResult.Direction == KeyResultDirections.Increase
            ? keyResult.TargetValue - keyResult.BaselineValue
            : keyResult.BaselineValue - keyResult.TargetValue;
        var travelled = keyResult.Direction == KeyResultDirections.Increase
            ? keyResult.CurrentValue - keyResult.BaselineValue
            : keyResult.BaselineValue - keyResult.CurrentValue;
        if (distance <= 0) return 0;
        return Math.Clamp((int)Math.Round(travelled * 100m / distance), 0, 100);
    }
}
