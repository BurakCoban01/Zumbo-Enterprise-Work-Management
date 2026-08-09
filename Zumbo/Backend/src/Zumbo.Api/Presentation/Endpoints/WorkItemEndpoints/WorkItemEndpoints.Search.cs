using Zumbo.Api.Presentation.Endpoints.WorkItems.Search;

internal static partial class WorkItemEndpoints
{
    private static void MapPostSearch(RouteGroupBuilder group) => SearchWorkItemsPageEndpoint.Map(group);

    private static void MapPostSearchRebuild(RouteGroupBuilder group) => RebuildSearchIndexEndpoint.Map(group);

    private static void MapPostSearchReconcile(RouteGroupBuilder group) => ReconcileSearchIndexEndpoint.Map(group);
}
