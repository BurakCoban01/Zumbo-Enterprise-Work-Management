using Zumbo.Api.Presentation.Endpoints.WorkItems.Checklist;

internal static partial class WorkItemEndpoints
{
    private static void MapPostByIdChecklist(RouteGroupBuilder group)
    {
        AddChecklistItemEndpoint.Map(group);
    }

    private static void MapPatchByIdChecklistByItemId(RouteGroupBuilder group)
    {
        SetChecklistItemCompletionEndpoint.Map(group);
    }
}
