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

    public Task<WorkItemActivityPage<WorkItemStatusHistoryResponse>> ListTimelineAsync(
        string organizationId, string projectId, string workItemId, int page, int pageSize, CancellationToken ct) =>
        PageAsync(timeline,
            x => x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId,
            x => x.ChangedAt,
            x => new WorkItemStatusHistoryResponse(x.FromStatus, x.ToStatus, x.ChangedByUserId, x.ChangedAt),
            page, pageSize, ct);
}
