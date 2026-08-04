using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Workflows.Application.Mapping.AutomationRuns;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows.Application.Features.RunQueries;

public sealed class GetAutomationRunHandler(
    IDocumentRepository<AutomationRunDocument> runs,
    IAutomationProjectAccessChecker access)
{
    public async Task<AutomationRunResponse> HandleAsync(
        GetAutomationRunQuery query,
        CancellationToken ct)
    {
        var run = await runs.SelectAsync(candidate => candidate.Id == query.RunId, ct)
            ?? throw new NotFoundException("AUTOMATION_RUN_NOT_FOUND", "Automation run was not found.");
        var scope = await access.EnsureCanViewAsync(run.ProjectId, ct);
        if (!run.OrganizationId.Equals(scope.OrganizationId, StringComparison.Ordinal))
        {
            throw new NotFoundException("AUTOMATION_RUN_NOT_FOUND", "Automation run was not found.");
        }

        return AutomationRunResponseMapper.ToResponse(run);
    }
}
