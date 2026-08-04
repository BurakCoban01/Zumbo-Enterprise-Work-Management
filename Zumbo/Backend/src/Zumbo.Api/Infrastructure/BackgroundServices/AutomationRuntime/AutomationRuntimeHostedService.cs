using System.Security.Claims;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Workflows;
using Zumbo.Modules.Workflows.Application.Features.RunRetry;
using Zumbo.Modules.Workflows.Application.Features.RunResume;
using Zumbo.Modules.Workflows.Application.Features.ScheduleClaims;
using Zumbo.Modules.Workflows.Application.Features.RunExecution;
using Zumbo.Modules.WorkItems;

public sealed class AutomationRuntimeHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<AutomationRuntimeOptions> options,
    ILogger<AutomationRuntimeHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
            return;

        var interval = TimeSpan.FromSeconds(Math.Clamp(options.Value.IntervalSeconds, 5, 3600));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await ProcessIterationAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Automation runtime iteration failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task ProcessIterationAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var executeAutomation = services.GetRequiredService<ExecuteAutomationHandler>();
        var dueRetries = services.GetRequiredService<ListDueAutomationRetriesHandler>();
        var resumeRun = services.GetRequiredService<ResumeAutomationRunHandler>();
        var claimSchedules = services.GetRequiredService<ClaimDueSchedulesHandler>();
        var completeSchedule = services.GetRequiredService<CompleteScheduleClaimHandler>();
        var actors = services.GetRequiredService<AutomationActorContextRunner>();
        var sources = services.GetRequiredService<IAutomationScheduledSourceProvider>();
        var transactions = services.GetRequiredService<IDurableTransactionRunner>();
        var batchSize = Math.Clamp(options.Value.BatchSize, 1, 200);

        var retries = await dueRetries.HandleAsync(
            new ListDueAutomationRetriesQuery(batchSize),
            ct);
        foreach (var run in retries)
        {
            try
            {
                await transactions.ExecuteAsync(
                    "Workflows",
                    token => actors.RunAsync(
                        run.ActorUserId,
                        run.OrganizationId,
                        run.CorrelationId,
                        available => resumeRun.HandleAsync(
                            new ResumeAutomationRunCommand(run.RunId, available),
                            token),
                        token),
                    ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(
                    exception,
                    "Automation retry {RunId} could not be processed.",
                    run.RunId);
            }
        }

        var schedules = await transactions.ExecuteAsync(
            "Workflows",
            token => claimSchedules.HandleAsync(
                new ClaimDueSchedulesQuery(batchSize),
                token),
            ct);
        foreach (var schedule in schedules)
        {
            IReadOnlyCollection<AutomationScheduledSource> scheduledSources;
            try
            {
                scheduledSources = await sources.ListAsync(
                    schedule.ProjectId,
                    Math.Clamp(options.Value.MaximumScheduledSourcesPerRule, 1, 5000),
                    ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(
                    exception,
                    "Automation schedule {RuleId} sources could not be read.",
                    schedule.RuleId);
                continue;
            }

            var allSourcesDispatched = true;
            foreach (var source in scheduledSources)
            {
                try
                {
                    var correlationId =
                        $"automation-schedule:{schedule.RuleId}:{schedule.ScheduledForUtc:O}";
                    await transactions.ExecuteAsync(
                        "Workflows",
                        token => actors.RunAsync(
                            schedule.ActorUserId,
                            schedule.OrganizationId,
                            correlationId,
                            available => executeAutomation.HandleAsync(
                                new ExecuteAutomationCommand(new AutomationExecutionContext(
                                    schedule.OrganizationId,
                                    schedule.ProjectId,
                                    "Schedule",
                                    null,
                                    $"{schedule.RuleId}:{schedule.RuleVersion}:{schedule.ScheduledForUtc:O}",
                                    source.SourceId,
                                    schedule.ActorUserId,
                                    correlationId,
                                    schedule.ScheduledForUtc,
                                    source.Fields,
                                    available,
                                    RuleId: schedule.RuleId)),
                                token),
                            token),
                        ct);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    allSourcesDispatched = false;
                    logger.LogWarning(
                        exception,
                        "Automation schedule {RuleId} source {SourceId} failed.",
                        schedule.RuleId,
                        source.SourceId);
                }
            }

            if (allSourcesDispatched)
            {
                await transactions.ExecuteAsync(
                    "Workflows",
                    token => completeSchedule.HandleAsync(
                        new CompleteScheduleClaimCommand(
                            schedule.RuleId,
                            schedule.ScheduledForUtc,
                            schedule.ClaimToken),
                        token),
                    ct);
            }
        }
    }
}
