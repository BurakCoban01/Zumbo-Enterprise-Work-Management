using Zumbo.Api.Presentation.Endpoints.WorkItems.Relations;

internal static partial class WorkItemEndpoints
{
    private static void MapPostByIdRelations(RouteGroupBuilder group)
    {
        LinkWorkItemEndpoint.Map(group);
    }

    private static void MapDeleteByIdRelationsByRelatedWorkItemId(RouteGroupBuilder group)
    {
        UnlinkWorkItemEndpoint.Map(group);
    }
}
