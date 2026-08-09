using Zumbo.Api.Presentation.Endpoints.WorkItems.Schema;

internal static partial class WorkItemEndpoints
{
    private static void MapPutByIdCustomFields(RouteGroupBuilder group) => SetWorkItemCustomFieldsEndpoint.Map(group);
}
