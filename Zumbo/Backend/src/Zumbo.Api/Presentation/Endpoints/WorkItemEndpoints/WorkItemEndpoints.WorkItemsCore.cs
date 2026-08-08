using Zumbo.Api.Presentation.Endpoints.WorkItems.WorkItemsCore;

internal static partial class WorkItemEndpoints
{
    private static void MapDeleteById(RouteGroupBuilder group) => ArchiveWorkItemEndpoint.Map(group);

    private static void MapGetById(RouteGroupBuilder group) => GetWorkItemEndpoint.Map(group);

    private static void MapGetRoot(RouteGroupBuilder group) => SearchWorkItemsEndpoint.Map(group);

    private static void MapPatchByIdStatus(RouteGroupBuilder group) => MoveWorkItemEndpoint.Map(group);

    private static void MapPostByIdRestore(RouteGroupBuilder group) => RestoreWorkItemEndpoint.Map(group);

    private static void MapPostRoot(RouteGroupBuilder group) => CreateWorkItemEndpoint.Map(group);

    private static void MapPutById(RouteGroupBuilder group) => UpdateWorkItemEndpoint.Map(group);
}
