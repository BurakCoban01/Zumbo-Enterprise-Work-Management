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

public interface IWorkItemTeamPolicy
{
    Task EnsureCanAssignAsync(
        string projectId,
        string teamId,
        string? assigneeUserId,
        CancellationToken ct);
    Task<IReadOnlyCollection<WorkItemTeamEntry>> ListProjectTeamsAsync(string projectId, CancellationToken ct);
}
