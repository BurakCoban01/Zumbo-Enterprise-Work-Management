using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

namespace Zumbo.Api.Presentation.Endpoints.WorkItems.WorkItemsCore;

internal static class SearchWorkItemsEndpoint
{
    internal static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async (
            string? projectId,
            string? assigneeUserId,
            string? status,
            string? text,
            int? page,
            int? pageSize,
            bool? archived,
            string? issueType,
            string? customFieldKey,
            string? customFieldValue,
            SearchWorkItemsHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(
                new WorkItemSearchRequest(
                    projectId,
                    assigneeUserId,
                    status,
                    text,
                    page ?? 1,
                    pageSize ?? 100,
                    archived ?? false,
                    issueType,
                    customFieldKey,
                    customFieldValue),
                ct), http))
            .RequireRateLimiting("search");
    }
}
