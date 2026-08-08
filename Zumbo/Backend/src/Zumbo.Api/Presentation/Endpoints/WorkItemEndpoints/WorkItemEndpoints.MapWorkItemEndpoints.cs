using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;
using Zumbo.Api.Presentation.Endpoints.WorkItems.Attachments;
using Zumbo.Api.Presentation.Endpoints.WorkItems.Approvals;
using Zumbo.Api.Presentation.Endpoints.WorkItems.Checklist;
using Zumbo.Api.Presentation.Endpoints.WorkItems.Comments;
using Zumbo.Api.Presentation.Endpoints.WorkItems.Labels;
using Zumbo.Api.Presentation.Endpoints.WorkItems.Planning;
using Zumbo.Api.Presentation.Endpoints.WorkItems.Relations;
using Zumbo.Api.Presentation.Endpoints.WorkItems.Worklogs;

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

        MapGetDurableMessagingMetrics(group);

        MapGetDurableMessagingDeadLetters(group);

        MapPostDurableMessagingDeadLetterByMessageIdReplay(group);

        MapPostSearchRebuild(group);

        MapPostSearchReconcile(group);

        MapPostSearch(group);

        MapGetRoot(group);

        MapGetById(group);

        MapGetByIdCollaboration(group);

        MapPutByIdWatch(group);

        MapPutByIdVote(group);

        MapGetByIdActivity(group);

        MapGetTemplates(group);

        MapPostTemplates(group);

        MapPutTemplatesByTemplateId(group);

        MapDeleteTemplatesByTemplateId(group);

        MapGetRecurrences(group);

        MapPostRecurrences(group);

        MapPostRecurrencesPreview(group);

        MapPatchRecurrencesByRecurrenceIdState(group);

        MapDeleteRecurrencesByRecurrenceId(group);

        MapGetRecurrencesByRecurrenceIdOccurrences(group);

        MapPostRecurrencesProcessDue(group);

        MapPostRoot(group);

        MapPostBulkJobsImport(group);

        MapPostBulkJobsExport(group);

        MapPostBulkJobs(group);

        MapGetBulkJobs(group);

        MapGetBulkJobsByJobId(group);

        MapPostBulkJobsByJobIdCancel(group);

        MapPostBulkJobsByJobIdRetry(group);

        MapGetBulkJobsByJobIdResult(group);

        MapGetBulkJobsByJobIdErrors(group);

        MapPostBulkMove(group);

        MapPostBulkAssign(group);

        MapPostBulkArchive(group);

        MapPutById(group);

        AssignWorkItemEndpoint.Map(group);

        MapPatchByIdStatus(group);

        ReorderWorkItemEndpoint.Map(group);

        SetPlanningEndpoint.Map(group);

        MapPutByIdCustomFields(group);

        SetParentEndpoint.Map(group);

        SetTeamEndpoint.Map(group);

        RequestApprovalEndpoint.Map(group);

        DecideApprovalEndpoint.Map(group);

        AddChecklistItemEndpoint.Map(group);

        SetChecklistItemCompletionEndpoint.Map(group);

        AddLabelEndpoint.Map(group);

        RemoveLabelEndpoint.Map(group);

        AddCommentEndpoint.Map(group);

        ListCommentsEndpoint.Map(group);

        ListCommentRevisionsEndpoint.Map(group);

        EditCommentEndpoint.Map(group);

        DeleteCommentEndpoint.Map(group);

        UploadAttachmentEndpoint.Map(group);

        DownloadAttachmentEndpoint.Map(group);

        PreviewAttachmentEndpoint.Map(group);

        DeleteAttachmentEndpoint.Map(group);

        ListAttachmentsEndpoint.Map(group);

        AddWorkLogEndpoint.Map(group);

        ListWorkLogsEndpoint.Map(group);

        ListApprovalsEndpoint.Map(group);

        MapGetByIdTimeline(group);

        LinkWorkItemEndpoint.Map(group);

        UnlinkWorkItemEndpoint.Map(group);

        MapDeleteById(group);

        MapPostByIdRestore(group);

        MapGetReportsProjectSummaryByProjectId(group);

        MapGetReportsStatusDistributionByProjectId(group);

        MapGetReportsUserWorkloadByProjectId(group);

        MapGetReportsDueDateRisksByProjectId(group);

        MapGetReportsSprintBurndownByProjectIdBySprintId(group);

        MapGetReportsSprintVelocityByProjectId(group);

        MapGetReportsFlowTimeByProjectId(group);

        MapGetReportsCompletionRateByProjectId(group);

        MapGetReportsTeamPerformanceByProjectId(group);
    }
}
