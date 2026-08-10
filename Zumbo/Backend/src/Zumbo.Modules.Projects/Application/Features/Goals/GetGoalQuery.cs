namespace Zumbo.Modules.Projects.Application.Features.Goals;

public sealed record GetGoalQuery(string GoalId, bool IncludeArchived);
