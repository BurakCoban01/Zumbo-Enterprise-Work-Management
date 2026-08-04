using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;
using Zumbo.SharedKernel;

public sealed class AutomationProjectAccessCheckerAdapter(
    IProjectResourcePolicy resourcePolicy) : IAutomationProjectAccessChecker
{
    public Task<AutomationProjectScope> EnsureCanViewAsync(string projectId, CancellationToken ct) =>
        EnsureAsync(projectId, manage: false, ct);

    public Task<AutomationProjectScope> EnsureCanManageAsync(string projectId, CancellationToken ct) =>
        EnsureAsync(projectId, manage: true, ct);

    private async Task<AutomationProjectScope> EnsureAsync(
        string projectId,
        bool manage,
        CancellationToken ct)
    {
        var authorization = await resourcePolicy.AuthorizeAsync(
            projectId,
            manage ? PermissionCatalog.WorkflowManage : PermissionCatalog.WorkflowView,
            ct);
        return new AutomationProjectScope(
            authorization.OrganizationId,
            authorization.UserId);
    }
}
