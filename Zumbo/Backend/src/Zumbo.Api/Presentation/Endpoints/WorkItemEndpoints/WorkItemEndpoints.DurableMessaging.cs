using Zumbo.Api.Presentation.Endpoints.WorkItems.Reports;
using Zumbo.Api.Presentation.Endpoints.WorkItems.WorkItemsCore;

internal static partial class WorkItemEndpoints
{
    private static void MapGetDurableMessagingMetrics(RouteGroupBuilder group) => GetDurableMessagingMetricsEndpoint.Map(group);

    private static void MapGetDurableMessagingDeadLetters(RouteGroupBuilder group) => ListDurableMessageDeadLettersEndpoint.Map(group);

    private static void MapPostDurableMessagingDeadLetterByMessageIdReplay(RouteGroupBuilder group) => ReplayDurableMessageDeadLetterEndpoint.Map(group);
}
