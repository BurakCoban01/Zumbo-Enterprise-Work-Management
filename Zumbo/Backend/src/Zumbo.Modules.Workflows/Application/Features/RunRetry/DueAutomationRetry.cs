namespace Zumbo.Modules.Workflows.Application.Features.RunRetry;

public sealed record DueAutomationRetry(
    string RunId,
    string OrganizationId,
    string ActorUserId,
    string CorrelationId);
