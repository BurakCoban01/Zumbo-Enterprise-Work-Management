using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning.Snapshots;

internal sealed record SnapshotSource(
    IReadOnlyCollection<CapacityProjectAccess> Projects,
    IReadOnlyCollection<string> UnavailableProjectIds,
    IReadOnlyCollection<WorkItemDocument> Tasks,
    bool Truncated);
