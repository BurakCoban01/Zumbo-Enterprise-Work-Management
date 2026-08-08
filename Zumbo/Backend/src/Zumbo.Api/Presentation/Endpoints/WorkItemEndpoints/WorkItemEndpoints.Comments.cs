using Zumbo.Api.Presentation.Endpoints.WorkItems.Comments;

internal static partial class WorkItemEndpoints
{
    private static void MapPostByIdComments(RouteGroupBuilder group)
    {
        AddCommentEndpoint.Map(group);
    }

    private static void MapGetByIdComments(RouteGroupBuilder group)
    {
        ListCommentsEndpoint.Map(group);
    }

    private static void MapGetByIdCommentsByCommentIdRevisions(RouteGroupBuilder group)
    {
        ListCommentRevisionsEndpoint.Map(group);
    }

    private static void MapPutByIdCommentsByCommentId(RouteGroupBuilder group)
    {
        EditCommentEndpoint.Map(group);
    }

    private static void MapDeleteByIdCommentsByCommentId(RouteGroupBuilder group)
    {
        DeleteCommentEndpoint.Map(group);
    }
}
