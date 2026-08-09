namespace Zumbo.Modules.Projects.Application.Features.Goals;

public sealed record SaveKeyResultCommand(
    string GoalId,
    string? KeyResultId,
    SaveKeyResultRequest Request,
    string CorrelationId);
