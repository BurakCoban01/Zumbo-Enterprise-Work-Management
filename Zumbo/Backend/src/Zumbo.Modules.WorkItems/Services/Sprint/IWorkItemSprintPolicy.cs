using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public interface IWorkItemSprintPolicy
{
    Task EnsurePlanningAllowedAsync(
        string projectId,
        string? currentSprintId,
        string? targetSprintId,
        CancellationToken ct);
}
