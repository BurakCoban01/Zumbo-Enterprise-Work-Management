using Zumbo.Api.Presentation.Endpoints.WorkItems.Reports;
using Zumbo.Api.Presentation.Endpoints.WorkItems.Sprints;

internal static partial class WorkItemEndpoints
{
    private static void MapGetReportsProjectSummaryByProjectId(RouteGroupBuilder group) => GetProjectSummaryReportEndpoint.Map(group);

    private static void MapGetReportsStatusDistributionByProjectId(RouteGroupBuilder group) => GetStatusDistributionReportEndpoint.Map(group);

    private static void MapGetReportsUserWorkloadByProjectId(RouteGroupBuilder group) => GetUserWorkloadReportEndpoint.Map(group);

    private static void MapGetReportsDueDateRisksByProjectId(RouteGroupBuilder group) => GetDueDateRisksReportEndpoint.Map(group);

    private static void MapGetReportsSprintBurndownByProjectIdBySprintId(RouteGroupBuilder group) => GetSprintBurndownReportEndpoint.Map(group);

    private static void MapGetReportsSprintVelocityByProjectId(RouteGroupBuilder group) => GetSprintVelocityReportEndpoint.Map(group);

    private static void MapGetReportsFlowTimeByProjectId(RouteGroupBuilder group) => GetFlowTimeReportEndpoint.Map(group);

    private static void MapGetReportsCompletionRateByProjectId(RouteGroupBuilder group) => GetCompletionRateReportEndpoint.Map(group);

    private static void MapGetReportsTeamPerformanceByProjectId(RouteGroupBuilder group) => GetTeamPerformanceReportEndpoint.Map(group);
}
