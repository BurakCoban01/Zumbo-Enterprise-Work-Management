using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    private static CapacityTaskResponse ToTask(WorkItemDocument item) => new(
        item.Id,
        item.ProjectId,
        item.Title,
        item.AssigneeUserId,
        item.DueDate is null ? null : DateOnlyUtc(item.DueDate.Value),
        item.EstimatePoints <= 0 ? null : item.EstimatePoints);
}
