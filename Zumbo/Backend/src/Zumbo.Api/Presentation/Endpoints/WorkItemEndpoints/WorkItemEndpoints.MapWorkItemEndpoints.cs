using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;
using Zumbo.Api.Presentation.Endpoints.WorkItems.Attachments;
using Zumbo.Api.Presentation.Endpoints.WorkItems.BulkOperations;
using Zumbo.Api.Presentation.Endpoints.WorkItems.Planning;
using Zumbo.Api.Presentation.Endpoints.WorkItems.Recurrences;
using Zumbo.Api.Presentation.Endpoints.WorkItems.Reports;
using Zumbo.Api.Presentation.Endpoints.WorkItems.Search;
using Zumbo.Api.Presentation.Endpoints.WorkItems.Schema;
using Zumbo.Api.Presentation.Endpoints.WorkItems.Sprints;
using Zumbo.Api.Presentation.Endpoints.WorkItems.WorkItemsCore;

using static ApiEndpointResults;

internal static partial class WorkItemEndpoints
{

    internal static void MapWorkItemEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/work-items")
            .WithTags("WorkItems")
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.WorkItemView);
        group.AddEndpointFilter<WorkItemTransactionFilter>();

        GetDurableMessagingMetricsEndpoint.Map(group);

        ListDurableMessageDeadLettersEndpoint.Map(group);

        ReplayDurableMessageDeadLetterEndpoint.Map(group);

        RebuildSearchIndexEndpoint.Map(group);

        ReconcileSearchIndexEndpoint.Map(group);

        SearchWorkItemsPageEndpoint.Map(group);

        SearchWorkItemsEndpoint.Map(group);

        GetWorkItemEndpoint.Map(group);

        ListTemplatesEndpoint.Map(group);

        CreateTemplateEndpoint.Map(group);

        UpdateTemplateEndpoint.Map(group);

        DeleteTemplateEndpoint.Map(group);

        ListRecurrencesEndpoint.Map(group);

        CreateRecurrenceEndpoint.Map(group);

        PreviewRecurrenceEndpoint.Map(group);

        SetRecurrenceStateEndpoint.Map(group);

        DeleteRecurrenceEndpoint.Map(group);

        ListRecurrenceOccurrencesEndpoint.Map(group);

        ProcessDueRecurrencesEndpoint.Map(group);

        CreateWorkItemEndpoint.Map(group);

        CreateBulkImportJobEndpoint.Map(group);

        CreateBulkExportJobEndpoint.Map(group);

        CreateBulkJobEndpoint.Map(group);

        ListBulkJobsEndpoint.Map(group);

        GetBulkJobEndpoint.Map(group);

        CancelBulkJobEndpoint.Map(group);

        RetryBulkJobEndpoint.Map(group);

        BulkMoveWorkItemsEndpoint.Map(group);

        BulkAssignWorkItemsEndpoint.Map(group);

        BulkArchiveWorkItemsEndpoint.Map(group);

        UpdateWorkItemEndpoint.Map(group);

        AssignWorkItemEndpoint.Map(group);

        MoveWorkItemEndpoint.Map(group);

        ReorderWorkItemEndpoint.Map(group);

        SetPlanningEndpoint.Map(group);

        SetWorkItemCustomFieldsEndpoint.Map(group);

        SetParentEndpoint.Map(group);

        SetTeamEndpoint.Map(group);

        ListAttachmentsEndpoint.Map(group);

        ArchiveWorkItemEndpoint.Map(group);

        RestoreWorkItemEndpoint.Map(group);

        GetProjectSummaryReportEndpoint.Map(group);

        GetStatusDistributionReportEndpoint.Map(group);

        GetUserWorkloadReportEndpoint.Map(group);

        GetDueDateRisksReportEndpoint.Map(group);

        GetSprintBurndownReportEndpoint.Map(group);

        GetSprintVelocityReportEndpoint.Map(group);

        GetFlowTimeReportEndpoint.Map(group);

        GetCompletionRateReportEndpoint.Map(group);

        GetTeamPerformanceReportEndpoint.Map(group);
    }
}
