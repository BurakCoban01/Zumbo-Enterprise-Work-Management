namespace Zumbo.Modules.Projects.Application.Features.Goals;

public sealed record AddKeyResultProgressCommand(
    string GoalId,
    string KeyResultId,
    AddKeyResultProgressRequest Request,
    string CorrelationId);
