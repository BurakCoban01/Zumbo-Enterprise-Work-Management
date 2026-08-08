using Zumbo.Api.Presentation.Endpoints.WorkItems.Planning;

internal static partial class WorkItemEndpoints
{
    private static void MapPatchByIdAssignee(RouteGroupBuilder group) => AssignWorkItemEndpoint.Map(group);

    private static void MapPatchByIdParent(RouteGroupBuilder group) => SetParentEndpoint.Map(group);

    private static void MapPatchByIdPlanning(RouteGroupBuilder group) => SetPlanningEndpoint.Map(group);

    private static void MapPatchByIdRank(RouteGroupBuilder group) => ReorderWorkItemEndpoint.Map(group);

    private static void MapPatchByIdTeam(RouteGroupBuilder group) => SetTeamEndpoint.Map(group);
}
