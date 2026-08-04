using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record CapacityAllocationRequest(
    string? Id,
    string UserId,
    string ProjectId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal Percent);
