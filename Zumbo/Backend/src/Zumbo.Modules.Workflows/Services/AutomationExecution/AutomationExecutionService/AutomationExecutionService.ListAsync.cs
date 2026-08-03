using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService{

    public async Task<AutomationRunPageResponse> ListAsync(
        string projectId,
        string? ruleId,
        string? status,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var scope = await access.EnsureCanViewAsync(projectId, ct);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var normalizedRuleId = string.IsNullOrWhiteSpace(ruleId) ? null : ruleId.Trim();
        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
        var filter = (System.Linq.Expressions.Expression<Func<AutomationRunDocument, bool>>)(run =>
            run.OrganizationId == scope.OrganizationId
            && run.ProjectId == projectId
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
            documents.Select(ToResponse).ToArray(),
            page,
            pageSize,
            total);
    }
}
