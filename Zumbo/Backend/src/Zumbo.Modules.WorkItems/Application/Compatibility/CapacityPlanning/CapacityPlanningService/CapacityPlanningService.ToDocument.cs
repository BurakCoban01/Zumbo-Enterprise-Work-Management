using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    private static CapacityAllocationDocument ToDocument(CapacityAllocationRequest item) => new()
    {
        Id = item.Id!,
        UserId = item.UserId,
        ProjectId = item.ProjectId,
        StartDateUtc = UtcDay(item.StartDate),
        EndDateUtc = UtcDay(item.EndDate),
        Percent = item.Percent
    };
}
