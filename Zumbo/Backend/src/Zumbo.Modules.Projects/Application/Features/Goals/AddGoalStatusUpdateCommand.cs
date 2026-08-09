namespace Zumbo.Modules.Projects.Application.Features.Goals;

public sealed record AddGoalStatusUpdateCommand(
    string GoalId,
    AddGoalStatusUpdateRequest Request,
    string CorrelationId);
