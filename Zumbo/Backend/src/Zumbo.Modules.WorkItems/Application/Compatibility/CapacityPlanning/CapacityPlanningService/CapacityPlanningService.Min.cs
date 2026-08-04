using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{
    private static DateOnly Min(DateOnly left, DateOnly right) => left < right ? left : right;
}
