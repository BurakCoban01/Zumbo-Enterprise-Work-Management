using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    private static IReadOnlyCollection<CapacityAllocationRequest> NormalizeAllocations(
        IReadOnlyCollection<CapacityAllocationRequest> requested,
        IReadOnlySet<string> memberIds,
        IReadOnlySet<string> projectIds,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        if (requested.Count > MaximumAllocations)
            throw new ValidationException(
                $"Capacity plan cannot contain more than {MaximumAllocations} allocations.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        return requested.Select(item =>
        {
            var id = string.IsNullOrWhiteSpace(item.Id)
                ? Guid.NewGuid().ToString("N")
                : Required(item.Id, "Allocation id", 128);
            if (!ids.Add(id))
                throw new ValidationException("Capacity-plan allocation ids must be unique.");
            var userId = Required(item.UserId, "Allocation user", 128);
            var projectId = Required(item.ProjectId, "Allocation project", 128);
            if (!memberIds.Contains(userId))
                throw new ValidationException("Allocation user must belong to the capacity plan.");
            if (!projectIds.Contains(projectId))
                throw new ValidationException("Allocation project must belong to the capacity plan.");
            if (item.EndDate < item.StartDate
                || item.StartDate < periodStart
                || item.EndDate > periodEnd)
                throw new ValidationException("Allocation dates must fall within the capacity-plan period.");
            if (item.Percent is <= 0 or > 100)
                throw new ValidationException("Allocation percent must be greater than 0 and at most 100.");
            return item with
            {
                Id = id,
                UserId = userId,
                ProjectId = projectId,
                Percent = Round(item.Percent)
            };
        }).ToList();
    }
}
