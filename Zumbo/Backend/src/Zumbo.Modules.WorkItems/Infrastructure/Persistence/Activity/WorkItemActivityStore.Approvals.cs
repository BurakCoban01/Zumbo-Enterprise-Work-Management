using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemActivityStore : IWorkItemActivityStore
{
    public Task CreateApprovalAsync(WorkItemApprovalActivityDocument approval, CancellationToken ct) =>
        CreateOwnedAsync(approvals, approval, ct);

    public Task UpdateApprovalAsync(WorkItemApprovalActivityDocument approval, CancellationToken ct) =>
        ReplaceOwnedAsync(approvals, approval, ct);

    public Task<WorkItemApprovalActivityDocument?> GetApprovalAsync(
        string organizationId, string projectId, string workItemId, string approvalId, CancellationToken ct) =>
        approvals.SelectAsync(x => x.Id == approvalId
            && x.OrganizationId == organizationId
            && x.ProjectId == projectId
            && x.WorkItemId == workItemId, ct);

    public Task<WorkItemActivityPage<WorkItemApprovalResponse>> ListApprovalsAsync(
        string organizationId, string projectId, string workItemId, int page, int pageSize, CancellationToken ct) =>
        PageAsync(approvals,
            x => x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId,
            x => x.RequestedAt,
            x => new WorkItemApprovalResponse(x.Id, x.FromStatus, x.ToStatus, x.RequestedByUserId,
                x.RequestedAt, x.ExpiresAt, x.Status, x.DecidedByUserId, x.DecidedAt, x.Note, x.ConsumedAt),
            page, pageSize, ct);
}
