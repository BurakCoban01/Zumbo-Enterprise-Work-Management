using Zumbo.Api.Presentation.Endpoints.WorkItems.Approvals;

internal static partial class WorkItemEndpoints
{
    private static void MapPostByIdApprovals(RouteGroupBuilder group) => RequestApprovalEndpoint.Map(group);

    private static void MapPostByIdApprovalsByApprovalIdDecision(RouteGroupBuilder group) => DecideApprovalEndpoint.Map(group);

    private static void MapGetByIdApprovals(RouteGroupBuilder group) => ListApprovalsEndpoint.Map(group);
}
