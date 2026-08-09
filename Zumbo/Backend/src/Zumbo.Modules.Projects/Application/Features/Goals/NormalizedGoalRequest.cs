namespace Zumbo.Modules.Projects.Application.Features.Goals;

internal sealed record NormalizedGoalRequest(
    string Name,
    string? Description,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    List<string> ViewerUserIds,
    List<GoalInitiativeLinkRequest> InitiativeLinks,
    List<string> ProjectIds);
