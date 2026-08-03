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

public sealed partial class WorkItemActivityStore{

    public Task<WorkItemApprovalActivityDocument?> GetApprovalAsync(
        string organizationId, string projectId, string workItemId, string approvalId, CancellationToken ct) =>
        approvals.SelectAsync(x => x.Id == approvalId
            && x.OrganizationId == organizationId
            && x.ProjectId == projectId
            && x.WorkItemId == workItemId, ct);
}
