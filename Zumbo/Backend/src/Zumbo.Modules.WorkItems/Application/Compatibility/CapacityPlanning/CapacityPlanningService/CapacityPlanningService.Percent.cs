using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    private static int Percent(decimal allocated, decimal capacity) =>
        capacity <= 0
            ? allocated > 0 ? 100 : 0
            : (int)Math.Round(allocated / capacity * 100m, MidpointRounding.AwayFromZero);
}
