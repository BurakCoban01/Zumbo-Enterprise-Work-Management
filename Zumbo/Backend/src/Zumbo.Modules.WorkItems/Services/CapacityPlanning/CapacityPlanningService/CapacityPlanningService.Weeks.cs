using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    private static IReadOnlyCollection<DateOnly> Weeks(DateOnly start, DateOnly end)
    {
        var mondayOffset = ((int)start.DayOfWeek + 6) % 7;
        var current = start.AddDays(-mondayOffset);
        var result = new List<DateOnly>();
        while (current <= end)
        {
            result.Add(current);
            current = current.AddDays(7);
        }
        return result;
    }
}
