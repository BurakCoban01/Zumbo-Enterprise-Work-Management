using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    private static string State(decimal allocated, decimal capacity)
    {
        if (capacity <= 0)
            return allocated > 0 ? CapacityLoadStates.OverCapacity : CapacityLoadStates.Available;
        var ratio = allocated / capacity;
        return ratio > 1m
            ? CapacityLoadStates.OverCapacity
            : ratio > 0.8m
                ? CapacityLoadStates.NearCapacity
                : CapacityLoadStates.Available;
    }
}
