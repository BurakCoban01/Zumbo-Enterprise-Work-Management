using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;
using Zumbo.Api.Presentation.Endpoints.WorkItems.Checklist;
using Zumbo.Api.Presentation.Endpoints.WorkItems.Comments;
using Zumbo.Api.Presentation.Endpoints.WorkItems.Labels;

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

        MapPatchByIdAssignee(group);

        MapPatchByIdStatus(group);

        MapPatchByIdRank(group);

        MapPatchByIdPlanning(group);

        MapPutByIdCustomFields(group);

        MapPatchByIdParent(group);

        MapPatchByIdTeam(group);

        MapPostByIdApprovals(group);

        MapPostByIdApprovalsByApprovalIdDecision(group);

        AddChecklistItemEndpoint.Map(group);

        SetChecklistItemCompletionEndpoint.Map(group);

        AddLabelEndpoint.Map(group);

        RemoveLabelEndpoint.Map(group);

        AddCommentEndpoint.Map(group);

        ListCommentsEndpoint.Map(group);

        ListCommentRevisionsEndpoint.Map(group);

        EditCommentEndpoint.Map(group);

        DeleteCommentEndpoint.Map(group);

        MapPostByIdAttachmentsUpload(group);

        MapGetByIdAttachmentsByAttachmentIdDownload(group);

        MapGetByIdAttachmentsByAttachmentIdPreview(group);

        MapDeleteByIdAttachmentsByAttachmentId(group);

        MapGetByIdAttachments(group);

        MapPostByIdWorklogs(group);

        MapGetByIdWorklogs(group);

        MapGetByIdApprovals(group);

        MapGetByIdTimeline(group);

        MapPostByIdRelations(group);

        MapDeleteByIdRelationsByRelatedWorkItemId(group);

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
