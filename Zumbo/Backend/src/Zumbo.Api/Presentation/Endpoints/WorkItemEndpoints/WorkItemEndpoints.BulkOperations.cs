using Zumbo.Api.Presentation.Endpoints.WorkItems.BulkOperations;

internal static partial class WorkItemEndpoints
{
    private static void MapPostBulkMove(RouteGroupBuilder group) => BulkMoveWorkItemsEndpoint.Map(group);

    private static void MapPostBulkAssign(RouteGroupBuilder group) => BulkAssignWorkItemsEndpoint.Map(group);

    private static void MapPostBulkArchive(RouteGroupBuilder group) => BulkArchiveWorkItemsEndpoint.Map(group);

    private static void MapPostBulkJobs(RouteGroupBuilder group) => CreateBulkJobEndpoint.Map(group);

    private static void MapGetBulkJobs(RouteGroupBuilder group) => ListBulkJobsEndpoint.Map(group);

    private static void MapGetBulkJobsByJobId(RouteGroupBuilder group) => GetBulkJobEndpoint.Map(group);

    private static void MapGetBulkJobsByJobIdErrors(RouteGroupBuilder group) => ListBulkJobErrorsEndpoint.Map(group);

    private static void MapGetBulkJobsByJobIdResult(RouteGroupBuilder group) => GetBulkJobResultEndpoint.Map(group);

    private static void MapPostBulkJobsByJobIdCancel(RouteGroupBuilder group) => CancelBulkJobEndpoint.Map(group);

    private static void MapPostBulkJobsByJobIdRetry(RouteGroupBuilder group) => RetryBulkJobEndpoint.Map(group);

    private static void MapPostBulkJobsExport(RouteGroupBuilder group) => CreateBulkExportJobEndpoint.Map(group);

    private static void MapPostBulkJobsImport(RouteGroupBuilder group) => CreateBulkImportJobEndpoint.Map(group);
}
