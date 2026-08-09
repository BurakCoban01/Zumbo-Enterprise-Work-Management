using Zumbo.Api.Presentation.Endpoints.WorkItems.Recurrences;

internal static partial class WorkItemEndpoints
{
    private static void MapGetRecurrences(RouteGroupBuilder group) => ListRecurrencesEndpoint.Map(group);

    private static void MapPostRecurrences(RouteGroupBuilder group) => CreateRecurrenceEndpoint.Map(group);

    private static void MapDeleteRecurrencesByRecurrenceId(RouteGroupBuilder group) => DeleteRecurrenceEndpoint.Map(group);

    private static void MapPostRecurrencesPreview(RouteGroupBuilder group) => PreviewRecurrenceEndpoint.Map(group);

    private static void MapPostRecurrencesProcessDue(RouteGroupBuilder group) => ProcessDueRecurrencesEndpoint.Map(group);

    private static void MapPatchRecurrencesByRecurrenceIdState(RouteGroupBuilder group) => SetRecurrenceStateEndpoint.Map(group);

    private static void MapGetRecurrencesByRecurrenceIdOccurrences(RouteGroupBuilder group) => ListRecurrenceOccurrencesEndpoint.Map(group);

    private static void MapGetTemplates(RouteGroupBuilder group) => ListTemplatesEndpoint.Map(group);

    private static void MapPostTemplates(RouteGroupBuilder group) => CreateTemplateEndpoint.Map(group);

    private static void MapPutTemplatesByTemplateId(RouteGroupBuilder group) => UpdateTemplateEndpoint.Map(group);

    private static void MapDeleteTemplatesByTemplateId(RouteGroupBuilder group) => DeleteTemplateEndpoint.Map(group);
}
