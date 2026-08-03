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

    public async Task<WorkItemReportActivityData> ReadReportDataAsync(
        string organizationId,
        string projectId,
        CancellationToken ct)
    {
        ValidateOwnership(organizationId, projectId, "report");
        var projectWorkLogs = await LoadAllAsync(
            workLogs,
            x => x.OrganizationId == organizationId && x.ProjectId == projectId,
            ct);
        var projectTimeline = await LoadAllAsync(
            timeline,
            x => x.OrganizationId == organizationId && x.ProjectId == projectId,
            ct);

        return new WorkItemReportActivityData(
            projectWorkLogs
                .GroupBy(x => x.WorkItemId, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Sum(log => log.Hours), StringComparer.Ordinal),
            projectTimeline
                .GroupBy(x => x.WorkItemId, StringComparer.Ordinal)
                .ToDictionary(
                    x => x.Key,
                    x => (IReadOnlyList<WorkItemStatusHistoryResponse>)x
                        .OrderBy(entry => entry.ChangedAt)
                        .ThenBy(entry => entry.Id, StringComparer.Ordinal)
                        .Select(entry => new WorkItemStatusHistoryResponse(
                            entry.FromStatus,
                            entry.ToStatus,
                            entry.ChangedByUserId,
                            entry.ChangedAt))
                        .ToList(),
                    StringComparer.Ordinal));
    }
}
