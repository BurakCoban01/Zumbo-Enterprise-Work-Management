namespace Zumbo.Modules.Workflows.Application.Features.ScheduleClaims;

public sealed record CompleteScheduleClaimCommand(
    string RuleId,
    DateTimeOffset ScheduledForUtc,
    string ClaimToken);
