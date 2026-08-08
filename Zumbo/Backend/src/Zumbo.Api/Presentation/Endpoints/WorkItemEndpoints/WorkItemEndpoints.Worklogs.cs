using Zumbo.Api.Presentation.Endpoints.WorkItems.Worklogs;

internal static partial class WorkItemEndpoints
{
    private static void MapPostByIdWorklogs(RouteGroupBuilder group)
    {
        AddWorkLogEndpoint.Map(group);
    }

    private static void MapGetByIdWorklogs(RouteGroupBuilder group)
    {
        ListWorkLogsEndpoint.Map(group);
    }
}
