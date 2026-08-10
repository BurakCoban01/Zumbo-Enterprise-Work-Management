using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Sprints;
using Zumbo.SharedKernel;

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
        services.AddScoped<GetSprintHandler>(provider => new GetSprintHandler(
            provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<ListSprintsHandler>(provider => new ListSprintsHandler(
            provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<ListSprintBacklogHandler>(provider => new ListSprintBacklogHandler(
            provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>()));
        services.AddScoped<GetSprintBurndownHandler>(provider => new GetSprintBurndownHandler(
            provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),
            provider.GetRequiredService<IDocumentRepository<SprintScopeSnapshotDocument>>(),
            provider.GetRequiredService<IDocumentRepository<SprintCompletionSnapshotDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IOptions<SprintOptions>>(),
            provider.GetRequiredService<IWorkItemReadModelCache>(),
            provider.GetRequiredService<IOptions<WorkItemReadModelCacheOptions>>()));
        services.AddScoped<GetSprintVelocityHandler>(provider => new GetSprintVelocityHandler(
            provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IWorkItemReadModelCache>(),
            provider.GetRequiredService<IOptions<WorkItemReadModelCacheOptions>>()));
        services.AddScoped<CreateSprintHandler>(provider => new CreateSprintHandler(
            provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>()));
        services.AddScoped<StartSprintHandler>(provider => new StartSprintHandler(
            provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),
            provider.GetRequiredService<IDocumentRepository<SprintScopeSnapshotDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IOptions<SprintOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<CompleteSprintHandler>(provider => new CompleteSprintHandler(
            provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),
            provider.GetRequiredService<IDocumentRepository<SprintScopeSnapshotDocument>>(),
            provider.GetRequiredService<IDocumentRepository<SprintCompletionSnapshotDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IOptions<SprintOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<PlanSprintWorkItemHandler>(provider => new PlanSprintWorkItemHandler(
            provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetService<IExpectedVersionAccessor>()));
        services.AddScoped<UnplanSprintWorkItemHandler>(provider => new UnplanSprintWorkItemHandler(
            provider.GetRequiredService<IDocumentRepository<SprintDocument>>(),
            provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),
            provider.GetRequiredService<IProjectPermissionChecker>(),
            provider.GetRequiredService<IWorkItemAuditPublisher>(),
            provider.GetRequiredService<IDistributedLockProvider>(),
            provider.GetRequiredService<IOptions<DistributedLockOptions>>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<ICurrentUser>(),
            provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),
            provider.GetService<IExpectedVersionAccessor>()));
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
                CreateSprintHandler handler,
                HttpContext http,
                CancellationToken ct) =>
            Created(await handler.HandleAsync(new CreateSprintCommand(request, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapGet("/{sprintId}", async (
                string sprintId,
                GetSprintHandler handler,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await handler.HandleAsync(new GetSprintQuery(sprintId), ct), http));

        group.MapGet("/projects/{projectId}", async (
                string projectId,
                string? after,
                int? pageSize,
                ListSprintsHandler handler,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new ListSprintsQuery(projectId, after, pageSize ?? 50),
                ct), http));

        group.MapGet("/projects/{projectId}/backlog", async (
                string projectId,
                string? after,
                int? pageSize,
                ListSprintBacklogHandler handler,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new ListSprintBacklogQuery(projectId, after, pageSize ?? 50),
                ct), http));

        group.MapPut("/{sprintId}/items/{workItemId}", async (
                string sprintId,
                string workItemId,
                PlanSprintWorkItemRequest request,
                PlanSprintWorkItemHandler handler,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new PlanSprintWorkItemCommand(sprintId, workItemId, request, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapDelete("/{sprintId}/items/{workItemId}", async (
                string sprintId,
                string workItemId,
                UnplanSprintWorkItemHandler handler,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new UnplanSprintWorkItemCommand(sprintId, workItemId, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapPost("/{sprintId}/start", async (
                string sprintId,
                StartSprintHandler handler,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await handler.HandleAsync(new StartSprintCommand(sprintId, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapPost("/{sprintId}/complete", async (
                string sprintId,
                CompleteSprintRequest request,
                CompleteSprintHandler handler,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new CompleteSprintCommand(sprintId, request, CorrelationId(http)), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapGet("/{sprintId}/burndown", async (
                string sprintId,
                DateOnly? startDate,
                DateOnly? endDate,
                GetSprintHandler getSprint,
                GetSprintBurndownHandler burndown,
                HttpContext http,
                CancellationToken ct) =>
        {
            var sprint = await getSprint.HandleAsync(new GetSprintQuery(sprintId), ct);
            var snapshot = await burndown.HandleAsync(
                new GetSprintBurndownQuery(sprint.ProjectId, sprintId, startDate, endDate),
                ct);
            return Ok(snapshot.Data, http);
        });

        group.MapGet("/projects/{projectId}/velocity", async (
                string projectId,
                int? sprintCount,
                GetSprintVelocityHandler handler,
                HttpContext http,
                CancellationToken ct) =>
            Ok((await handler.HandleAsync(
                new GetSprintVelocityQuery(projectId, sprintCount ?? 6),
                ct)).Data, http));
    }

}
