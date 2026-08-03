using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{
    private static DateOnly DateOnlyUtc(DateTimeOffset value) =>
        DateOnly.FromDateTime(value.UtcDateTime);
}
