using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.Modules.Workflows;
using Zumbo.Modules.Workflows.Application.Features.RunQueries;
using Zumbo.Modules.Workflows.Application.Features.RunReplay;
using Zumbo.Modules.Workflows.Application.Features.RunRetry;
using Zumbo.Modules.Workflows.Application.Features.ActionExecution;
using Zumbo.Modules.Workflows.Application.Features.RunResume;
using Zumbo.Modules.Workflows.Application.Features.ScheduleClaims;
using Zumbo.Modules.Workflows.Application.Features.RunExecution;

using static ApiEndpointResults;

internal static class AutomationEndpoints
{
    internal static IServiceCollection AddAutomationEngine(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        services.AddScoped<IAutomationProjectAccessChecker, AutomationProjectAccessCheckerAdapter>();
        services.AddScoped<IAutomationAuditWriter, AutomationAuditWriterAdapter>();
        services.AddScoped<IAutomationActionExecutor, AutomationWorkItemActionExecutor>();
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
        services.AddScoped<AutomationTransactionFilter>();
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

    internal static void MapAutomationEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/automations")
            .WithTags("Automations")
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.WorkflowView);
        group.AddEndpointFilter<AutomationTransactionFilter>();

        group.MapGet("", async (
            string projectId,
            bool? includeArchived,
            int? page,
            int? pageSize,
            AutomationRuleService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ListAsync(
                projectId,
                includeArchived ?? false,
                page ?? 1,
                pageSize ?? 50,
                ct), http));

        group.MapGet("/runs", async (
            string projectId,
            string? ruleId,
            string? status,
            int? page,
            int? pageSize,
            ListAutomationRunsHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(new ListAutomationRunsQuery(
                projectId,
                ruleId,
                status,
                page ?? 1,
                pageSize ?? 50), ct), http));

        group.MapGet("/runs/{runId}", async (
            string runId,
            GetAutomationRunHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(new GetAutomationRunQuery(runId), ct), http));

        group.MapPost("/runs/{runId}/replay", async (
            string runId,
            ReplayAutomationRunHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new ReplayAutomationRunCommand(runId, CorrelationId(http)),
                ct), http))
            .WithZumboPermission(PermissionCatalog.WorkflowManage);

        group.MapGet("/{ruleId}", async (
            string ruleId,
            bool? draft,
            AutomationRuleService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.GetAsync(ruleId, draft ?? false, ct), http));

        group.MapPost("", async (
            DefineAutomationRuleRequest request,
            AutomationRuleService service,
            HttpContext http,
            CancellationToken ct) =>
            Created(await service.SaveDraftAsync(null, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkflowManage);

        group.MapPut("/{ruleId}/draft", async (
            string ruleId,
            DefineAutomationRuleRequest request,
            AutomationRuleService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SaveDraftAsync(ruleId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkflowManage);

        group.MapPost("/{ruleId}/publish", async (
            string ruleId,
            AutomationRuleService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.PublishAsync(ruleId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkflowManage);

        group.MapPatch("/{ruleId}/state", async (
            string ruleId,
            SetAutomationStateRequest request,
            AutomationRuleService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.SetActiveAsync(ruleId, request.Active, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkflowManage);

        group.MapDelete("/{ruleId}", async (
            string ruleId,
            AutomationRuleService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            await service.ArchiveAsync(ruleId, CorrelationId(http), ct);
            return Results.NoContent();
        }).WithZumboPermission(PermissionCatalog.WorkflowManage);

        group.MapPost("/{ruleId}/dry-run", async (
            string ruleId,
            AutomationDryRunContext context,
            AutomationRuleService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.DryRunAsync(ruleId, context, ct), http))
            .WithZumboPermission(PermissionCatalog.WorkflowManage)
            .RequireRateLimiting("report");
    }

    internal sealed record SetAutomationStateRequest(bool Active);
}
