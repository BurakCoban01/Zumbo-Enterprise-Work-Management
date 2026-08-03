using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record CapacitySnapshotResponse(
    string PlanId,
    long PlanVersion,
    string SourceStatus,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTimeOffset GeneratedAt,
    bool Truncated,
    IReadOnlyCollection<string> UnavailableProjectIds,
    CapacitySnapshotSummaryResponse Summary,
    IReadOnlyCollection<CapacityMemberSnapshotResponse> Members,
    IReadOnlyCollection<CapacityTeamSnapshotResponse> Teams,
    IReadOnlyCollection<CapacityProjectSnapshotResponse> Projects);
