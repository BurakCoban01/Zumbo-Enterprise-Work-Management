using Zumbo.Api.Presentation.Endpoints.WorkItems.Realtime;

internal static partial class WorkItemEndpoints
{
    private static void MapGetByIdCollaboration(RouteGroupBuilder group) => GetCollaborationEndpoint.Map(group);

    private static void MapPutByIdVote(RouteGroupBuilder group) => SetVoteEndpoint.Map(group);

    private static void MapPutByIdWatch(RouteGroupBuilder group) => SetWatchEndpoint.Map(group);
}
