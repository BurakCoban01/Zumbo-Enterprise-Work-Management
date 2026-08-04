using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

using static ApiEndpointResults;

internal static class WorkItemTypeSchemaEndpoints
{
    internal static void MapWorkItemTypeSchemaEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/work-item-schemas")
            .WithTags("WorkItemSchemas")
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.WorkItemView);
        group.AddEndpointFilter<WorkItemTransactionFilter>();

        group.MapGet("/{projectId}", async (
                string projectId,
                WorkItemTypeSchemaService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.GetAsync(projectId, ct), http));

        group.MapPut("/{projectId}", async (
                string projectId,
                UpsertWorkItemTypeSchemaRequest request,
                WorkItemTypeSchemaService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.UpsertAsync(projectId, request, CorrelationId(http), ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapGet("/{projectId}/reports/issue-types", async (
                string projectId,
                WorkItemTypeSchemaService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.GetIssueTypeDistributionAsync(projectId, ct), http));

        group.MapGet("/{projectId}/reports/custom-fields/{fieldKey}", async (
                string projectId,
                string fieldKey,
                WorkItemTypeSchemaService service,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await service.GetCustomFieldDistributionAsync(projectId, fieldKey, ct), http));
    }
}
