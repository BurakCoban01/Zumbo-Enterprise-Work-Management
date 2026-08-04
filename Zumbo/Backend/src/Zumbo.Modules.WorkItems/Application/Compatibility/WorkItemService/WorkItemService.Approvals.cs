using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemService
{
    private async Task UpdateApprovalActivityAsync(
        WorkItemDocument workItem,
        WorkItemApprovalDocument approval,
        CancellationToken ct)
    {
        var organizationId = CurrentOrganizationId(workItem.ProjectId);
        var stored = WorkItemActivityStore.ToActivity(workItem, organizationId, approval);
        var current = await activityStore.GetApprovalAsync(
            organizationId, workItem.ProjectId, workItem.Id, approval.Id, ct);
        stored.Version = current?.Version
            ?? throw new NotFoundException("WORK_ITEM_APPROVAL_NOT_FOUND", "Work item approval was not found.");
        await activityStore.UpdateApprovalAsync(stored, ct);
    }
}
