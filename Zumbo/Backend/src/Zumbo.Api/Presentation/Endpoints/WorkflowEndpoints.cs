using Zumbo.Api.Composition.Modules.Workflows;
using Zumbo.Modules.Workflows;
using Zumbo.BuildingBlocks.Application.Security;

using static ApiEndpointResults;

internal static class WorkflowEndpoints
{
    internal static IServiceCollection AddWorkflowsModule(
        this IServiceCollection services,
        IConfiguration? configuration = null) =>
        services.AddWorkflowServices(configuration);

    internal static void MapWorkflowEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/workflows").WithTags("Workflows").RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.WorkflowView);

        group.MapGet("/{projectId}", async (string projectId, GetWorkflowHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(new GetWorkflowQuery(projectId), ct), http));

        group.MapGet("/{projectId}/draft", async (string projectId, WorkflowService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.GetDraftAsync(projectId, ct), http));

        group.MapGet("/{projectId}/versions", async (string projectId, WorkflowService service, HttpContext http, CancellationToken ct) =>
            Ok(await service.ListVersionsAsync(projectId, ct), http));

        group.MapPut("/{projectId}/draft", async (string projectId, CreateWorkflowRequest request, SaveWorkflowDraftHandler handler, HttpContext http, CancellationToken ct) =>
        {
            var normalized = request with { ProjectId = projectId };
            return Ok(await handler.HandleAsync(normalized, CorrelationId(http), ct), http);
        }).WithZumboPermission(PermissionCatalog.WorkflowManage);

        group.MapPost("/{projectId}/publish", async (string projectId, PublishWorkflowHandler handler, HttpContext http, CancellationToken ct) =>
            Ok(await handler.HandleAsync(projectId, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkflowManage);

        group.MapPut("/{projectId}", async (string projectId, CreateWorkflowRequest request, UpsertWorkflowHandler handler, HttpContext http, CancellationToken ct) =>
        {
            var normalized = request with { ProjectId = projectId };
            return Ok(await handler.HandleAsync(normalized, CorrelationId(http), ct), http);
        }).WithZumboPermission(PermissionCatalog.WorkflowManage);
    }
}
