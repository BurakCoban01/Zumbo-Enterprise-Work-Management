using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.CapacityPlanning.Scenarios;

internal static class CapacityScenarioValidator
{
    private const int MaximumAllocations = 500;

    public static IReadOnlyCollection<CapacityAllocationRequest> Validate(
        CapacityPlanDocument plan,
        CapacityScenarioRequest request)
    {
        var requested = request.Allocations
            ?? throw new ValidationException("Scenario allocations are required.");
        if (requested.Count > MaximumAllocations)
        {
            throw new ValidationException(
                $"Capacity plan cannot contain more than {MaximumAllocations} allocations.");
        }

        var memberIds = plan.Members
            .Select(item => item.UserId)
            .ToHashSet(StringComparer.Ordinal);
        var projectIds = plan.ProjectIds.ToHashSet(StringComparer.Ordinal);
        var periodStart = DateOnlyUtc(plan.PeriodStartUtc);
        var periodEnd = DateOnlyUtc(plan.PeriodEndUtc);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        return requested.Select(item =>
        {
            var id = string.IsNullOrWhiteSpace(item.Id)
                ? Guid.NewGuid().ToString("N")
                : Required(item.Id, "Allocation id", 128);
            if (!ids.Add(id))
            {
                throw new ValidationException(
                    "Capacity-plan allocation ids must be unique.");
            }

            var userId = Required(item.UserId, "Allocation user", 128);
            var projectId = Required(item.ProjectId, "Allocation project", 128);
            if (!memberIds.Contains(userId))
            {
                throw new ValidationException(
                    "Allocation user must belong to the capacity plan.");
            }

            if (!projectIds.Contains(projectId))
            {
                throw new ValidationException(
                    "Allocation project must belong to the capacity plan.");
            }

            if (item.EndDate < item.StartDate
                || item.StartDate < periodStart
                || item.EndDate > periodEnd)
            {
                throw new ValidationException(
                    "Allocation dates must fall within the capacity-plan period.");
            }

            if (item.Percent is <= 0 or > 100)
            {
                throw new ValidationException(
                    "Allocation percent must be greater than 0 and at most 100.");
            }

            return item with
            {
                Id = id,
                UserId = userId,
                ProjectId = projectId,
                Percent = Round(item.Percent)
            };
        }).ToList();
    }

    private static string Required(string? value, string label, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException($"{label} is required.");
        }

        var normalized = value.Trim();
        if (normalized.Length > maximum)
        {
            throw new ValidationException(
                $"{label} cannot exceed {maximum} characters.");
        }

        return normalized;
    }

    private static DateOnly DateOnlyUtc(DateTimeOffset value) =>
        DateOnly.FromDateTime(value.UtcDateTime);

    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
