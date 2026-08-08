using Zumbo.Api.Presentation.Endpoints.WorkItems.Labels;

internal static partial class WorkItemEndpoints
{
    private static void MapPostByIdLabels(RouteGroupBuilder group)
    {
        AddLabelEndpoint.Map(group);
    }

    private static void MapDeleteByIdLabelsByLabel(RouteGroupBuilder group)
    {
        RemoveLabelEndpoint.Map(group);
    }
}
