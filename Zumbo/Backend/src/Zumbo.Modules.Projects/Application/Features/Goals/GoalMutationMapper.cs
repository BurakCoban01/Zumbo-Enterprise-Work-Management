namespace Zumbo.Modules.Projects.Application.Features.Goals;

internal static class GoalMutationMapper
{
    internal static void Apply(
        GoalDocument goal,
        NormalizedGoalRequest request,
        DateTimeOffset now)
    {
        goal.Name = request.Name;
        goal.Description = request.Description;
        goal.PeriodStartAtUtc = AtStartOfDay(request.PeriodStart);
        goal.PeriodEndAtUtc = AtStartOfDay(request.PeriodEnd);
        goal.ViewerUserIds = request.ViewerUserIds;
        goal.InitiativeLinks = request.InitiativeLinks.Select(item =>
            new GoalInitiativeLinkDocument
            {
                PortfolioId = item.PortfolioId,
                InitiativeId = item.InitiativeId
            }).ToList();
        goal.ProjectIds = request.ProjectIds;
        goal.UpdatedAt = now;
    }

    internal static void Apply(KeyResultDocument keyResult, SaveKeyResultRequest request)
    {
        keyResult.Name = request.Name;
        keyResult.Description = request.Description;
        keyResult.OwnerUserId = request.OwnerUserId;
        keyResult.BaselineValue = request.BaselineValue;
        keyResult.TargetValue = request.TargetValue;
        keyResult.Unit = request.Unit;
        keyResult.Direction = request.Direction;
    }

    private static DateTimeOffset AtStartOfDay(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
