using Zumbo.Api.Presentation.Endpoints.WorkItems.Activity;

internal static partial class WorkItemEndpoints
{
    private static void MapGetByIdActivity(RouteGroupBuilder group) => GetWorkItemActivityEndpoint.Map(group);

    private static void MapGetByIdTimeline(RouteGroupBuilder group) => GetWorkItemTimelineEndpoint.Map(group);
}
