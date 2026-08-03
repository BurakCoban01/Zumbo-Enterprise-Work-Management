using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService(
    IDocumentRepository<AutomationRuleDocument> rules,
    IDocumentRepository<AutomationRunDocument> runs,
    IAutomationProjectAccessChecker access,
    IAutomationActionExecutor actions,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    IAutomationAuditWriter audit,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private const int MaximumRunAttempts = 3;
    private static readonly TimeSpan ScheduleClaimDuration = TimeSpan.FromMinutes(5);
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);
}
