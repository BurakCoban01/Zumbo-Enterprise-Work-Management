using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Schema;

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
                GetWorkItemTypeSchemaHandler handler,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await handler.HandleAsync(new GetWorkItemTypeSchemaQuery(projectId), ct), http));

        group.MapPut("/{projectId}", async (
                string projectId,
                UpsertWorkItemTypeSchemaRequest request,
                UpsertWorkItemTypeSchemaHandler handler,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new UpsertWorkItemTypeSchemaCommand(projectId, request, CorrelationId(http)),
                ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemUpdate);

        group.MapGet("/{projectId}/reports/issue-types", async (
                string projectId,
                GetIssueTypeDistributionHandler handler,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await handler.HandleAsync(new GetIssueTypeDistributionQuery(projectId), ct), http));

        group.MapGet("/{projectId}/reports/custom-fields/{fieldKey}", async (
                string projectId,
                string fieldKey,
                GetCustomFieldDistributionHandler handler,
                HttpContext http,
                CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new GetCustomFieldDistributionQuery(projectId, fieldKey),
                ct), http));
    }
}
