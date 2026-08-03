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

public sealed class WorkflowProjectAccessCheckerAdapter(
    IProjectResourcePolicy resourcePolicy) : IWorkflowProjectAccessChecker
{
    public Task EnsureCanViewAsync(string projectId, CancellationToken ct) =>
        EnsureAsync(projectId, manage: false, ct);

    public Task EnsureCanManageAsync(string projectId, CancellationToken ct) =>
        EnsureAsync(projectId, manage: true, ct);

    private async Task EnsureAsync(string projectId, bool manage, CancellationToken ct)
    {
        var permission = manage ? PermissionCatalog.WorkflowManage : PermissionCatalog.WorkflowView;
        _ = await resourcePolicy.AuthorizeAsync(projectId, permission, ct);
    }
}
