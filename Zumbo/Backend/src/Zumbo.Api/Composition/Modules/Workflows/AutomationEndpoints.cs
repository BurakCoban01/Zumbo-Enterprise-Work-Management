using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.Api.Composition.Modules.WorkItems;
using Zumbo.Modules.Workflows;
using Zumbo.Modules.Workflows.Application.Features.RunQueries;
using Zumbo.Modules.Workflows.Application.Features.RunReplay;
using Zumbo.Modules.Workflows.Application.Features.RunRetry;
using Zumbo.Modules.Workflows.Application.Features.ActionExecution;
using Zumbo.Modules.Workflows.Application.Features.RunResume;
using Zumbo.Modules.Workflows.Application.Features.ScheduleClaims;
using Zumbo.Modules.Workflows.Application.Features.RunExecution;

internal static class AutomationEndpoints
{
    internal static IServiceCollection AddAutomationEngine(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        services.AddScoped<IAutomationProjectAccessChecker, AutomationProjectAccessCheckerAdapter>();
        services.AddScoped<IAutomationAuditWriter, AutomationAuditWriterAdapter>();
        services.AddWorkItemAutomationActionAdapter();
        services.AddScoped<IAutomationScheduledSourceProvider, AutomationScheduledSourceProvider>();
        services.AddScoped<AutomationActorContextRunner>();
        services.AddScoped<AutomationRuleService>();
        services.AddScoped<AutomationExecutionService>();
        services.AddScoped<GetAutomationRunHandler>();
        services.AddScoped<ListAutomationRunsHandler>();
        services.AddScoped<ReplayAutomationRunHandler>();
        services.AddScoped<ListDueAutomationRetriesHandler>();
        services.AddScoped<AutomationRunActionExecutor>();
        services.AddScoped<ResumeAutomationRunHandler>();
        services.AddScoped<ClaimDueSchedulesHandler>();
        services.AddScoped<CompleteScheduleClaimHandler>();
        services.AddScoped<ExecuteAutomationHandler>();
        services.AddScoped<IDurableEventHandler, WorkItemAutomationDurableHandler>();
        services.AddOptions<AutomationRuntimeOptions>()
            .BindConfiguration("AutomationRuntime")
            .Validate(
                options => options.IntervalSeconds is >= 5 and <= 3600
                    && options.BatchSize is >= 1 and <= 200
                    && options.MaximumScheduledSourcesPerRule is >= 1 and <= 5000,
                "Automation runtime settings are outside the supported bounds.")
            .ValidateOnStart();
        if (configuration?.GetValue("BackgroundJobs:Enabled", true) == true)
            services.AddHostedService<AutomationRuntimeHostedService>();
        return services;
    }

}
