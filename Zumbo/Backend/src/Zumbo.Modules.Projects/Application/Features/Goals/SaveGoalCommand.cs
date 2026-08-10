namespace Zumbo.Modules.Projects.Application.Features.Goals;

public sealed record SaveGoalCommand(
    string? GoalId,
    SaveGoalRequest Request,
    string CorrelationId);
