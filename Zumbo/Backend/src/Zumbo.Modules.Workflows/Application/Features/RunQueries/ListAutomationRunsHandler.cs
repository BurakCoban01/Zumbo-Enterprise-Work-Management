using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Workflows.Application.Mapping.AutomationRuns;

namespace Zumbo.Modules.Workflows.Application.Features.RunQueries;

public sealed class ListAutomationRunsHandler(
    IDocumentRepository<AutomationRunDocument> runs,
    IAutomationProjectAccessChecker access)
{
    public async Task<AutomationRunPageResponse> HandleAsync(
        ListAutomationRunsQuery query,
        CancellationToken ct)
    {
        var scope = await access.EnsureCanViewAsync(query.ProjectId, ct);
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var normalizedRuleId = string.IsNullOrWhiteSpace(query.RuleId) ? null : query.RuleId.Trim();
        var normalizedStatus = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim();
        var filter = (System.Linq.Expressions.Expression<Func<AutomationRunDocument, bool>>)(run =>
            run.OrganizationId == scope.OrganizationId
            && run.ProjectId == query.ProjectId
            && (normalizedRuleId == null || run.RuleId == normalizedRuleId)
            && (normalizedStatus == null || run.Status == normalizedStatus));
        var total = await runs.CountByFilterAsync(filter, ct);
        var documents = await runs.ListByFilterAsync(
            filter,
            run => run.CreatedAtUtc,
            orderDescending: true,
            page,
            pageSize,
            cancellationToken: ct);
        return new AutomationRunPageResponse(
            documents.Select(AutomationRunResponseMapper.ToResponse).ToArray(),
            page,
            pageSize,
            total);
    }
}
