using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

using static ApiEndpointResults;

internal static class SprintEndpoints
{
    internal static IServiceCollection AddSprintsModule(this IServiceCollection services)
    {
        services.AddOptions<SprintOptions>()
            .BindConfiguration("Sprint")
            .Validate(
                options => options.BatchSize is >= 1 and <= 200
                    && options.MaxBatchesPerOperation is >= 1 and <= 10_000,
                "Sprint batch settings are outside the supported bounds.")
            .ValidateOnStart();
        services.AddScoped<IWorkItemSprintPolicy, WorkItemSprintPolicyAdapter>();
        services.AddScoped<SprintService>();
        return services;
    }

    internal static void MapSprintEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/sprints")
            .WithTags("Sprints")
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.WorkItemView);
        group.AddEndpointFilter<WorkItemTransactionFilter>();

        group.MapPost("", async (
                CreateSprintRequest request,
                SprintService service,
                HttpContext http,
                CancellationToken ct) =>
            Created(await service.CreateAsync(request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapGet("/{sprintId}", async (
                string sprintId,
                SprintService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.GetAsync(sprintId, ct), http));

        group.MapGet("/projects/{projectId}", async (
                string projectId,
                string? after,
                int? pageSize,
                SprintService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.ListAsync(projectId, after, pageSize ?? 50, ct), http));

        group.MapGet("/projects/{projectId}/backlog", async (
                string projectId,
                string? after,
                int? pageSize,
                SprintService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.BacklogAsync(projectId, after, pageSize ?? 50, ct), http));

        group.MapPut("/{sprintId}/items/{workItemId}", async (
                string sprintId,
                string workItemId,
                PlanSprintWorkItemRequest request,
                SprintService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.PlanAsync(sprintId, workItemId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapDelete("/{sprintId}/items/{workItemId}", async (
                string sprintId,
                string workItemId,
                SprintService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.UnplanAsync(sprintId, workItemId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapPost("/{sprintId}/start", async (
                string sprintId,
                SprintService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.StartAsync(sprintId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapPost("/{sprintId}/complete", async (
                string sprintId,
                CompleteSprintRequest request,
                SprintService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.CompleteAsync(sprintId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapGet("/{sprintId}/burndown", async (
                string sprintId,
                DateOnly? startDate,
                DateOnly? endDate,
                SprintService service,
                HttpContext http,
                CancellationToken ct) =>
        {
            var sprint = await service.GetAsync(sprintId, ct);
            return Ok(await service.BurndownAsync(sprint.ProjectId, sprintId, startDate, endDate, ct), http);
        });

        group.MapGet("/projects/{projectId}/velocity", async (
                string projectId,
                int? sprintCount,
                SprintService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.VelocityAsync(projectId, sprintCount ?? 6, ct), http));
    }

}
