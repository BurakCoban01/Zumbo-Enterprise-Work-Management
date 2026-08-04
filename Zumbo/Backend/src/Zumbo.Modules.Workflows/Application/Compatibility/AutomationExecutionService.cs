using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Workflows.Application.Features.RunQueries;
using Zumbo.Modules.Workflows.Application.Features.RunReplay;
using Zumbo.Modules.Workflows.Application.Features.ActionExecution;
using Zumbo.Modules.Workflows.Application.Features.RunResume;
using Zumbo.Modules.Workflows.Application.Features.ScheduleClaims;
using Zumbo.Modules.Workflows.Application.Features.RunExecution;
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
    private readonly GetAutomationRunHandler getAutomationRunHandler = new(runs, access);
    private readonly ListAutomationRunsHandler listAutomationRunsHandler = new(runs, access);
    private readonly ReplayAutomationRunHandler replayAutomationRunHandler = new(
        runs,
        access,
        distributedLockProvider,
        distributedLockOptions,
        clock,
        audit,
        expectedVersions);
    private readonly ResumeAutomationRunHandler resumeAutomationRunHandler = new(
        rules,
        runs,
        distributedLockProvider,
        distributedLockOptions,
        clock,
        new AutomationRunActionExecutor(runs, actions, clock));
    private readonly ClaimDueSchedulesHandler claimDueSchedulesHandler = new(
        rules,
        distributedLockProvider,
        distributedLockOptions,
        clock);
    private readonly CompleteScheduleClaimHandler completeScheduleClaimHandler = new(
        rules,
        distributedLockProvider,
        distributedLockOptions,
        clock);
    private readonly ExecuteAutomationHandler executeAutomationHandler = new(
        rules,
        runs,
        distributedLockProvider,
        distributedLockOptions,
        clock,
        new AutomationRunActionExecutor(runs, actions, clock));
}
