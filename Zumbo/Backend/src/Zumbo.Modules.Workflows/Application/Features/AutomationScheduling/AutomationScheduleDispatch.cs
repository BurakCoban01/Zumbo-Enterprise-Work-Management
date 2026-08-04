using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record AutomationScheduleDispatch(
    string RuleId,
    int RuleVersion,
    string RuleName,
    string OrganizationId,
    string ProjectId,
    string ActorUserId,
    DateTimeOffset ScheduledForUtc,
    string ClaimToken);
