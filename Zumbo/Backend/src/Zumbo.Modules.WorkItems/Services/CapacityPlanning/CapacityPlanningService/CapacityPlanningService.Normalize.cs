using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class CapacityPlanningService{

    private static SaveCapacityPlanRequest Normalize(SaveCapacityPlanRequest request)
    {
        if (request.PeriodEnd < request.PeriodStart)
            throw new ValidationException("Capacity-plan end date must be after start date.");
        if (request.PeriodEnd.DayNumber - request.PeriodStart.DayNumber + 1 > 366)
            throw new ValidationException("Capacity-plan period cannot exceed 366 days.");
        var projectIds = NormalizeIds(
            request.ProjectIds
                ?? throw new ValidationException("Capacity-plan project list is required."),
            MaximumProjects,
            "Capacity-plan project");
        if (projectIds.Count == 0)
            throw new ValidationException("Capacity plan must include at least one project.");
        var viewers = NormalizeIds(
            request.ViewerUserIds
                ?? throw new ValidationException("Capacity-plan viewer list is required."),
            MaximumViewers,
            "Capacity-plan viewer");
        var requestedMembers = request.Members
            ?? throw new ValidationException("Capacity-plan member list is required.");
        if (requestedMembers.Count is < 1 or > MaximumMembers)
            throw new ValidationException(
                $"Capacity plan must contain between 1 and {MaximumMembers} members.");
        var memberIds = new HashSet<string>(StringComparer.Ordinal);
        var members = requestedMembers.Select(member =>
        {
            var userId = Required(member.UserId, "Capacity member user", 128);
            if (!memberIds.Add(userId))
                throw new ValidationException("Capacity-plan member users must be unique.");
            if (member.WeeklyCapacityHours is < 0 or > 168)
                throw new ValidationException("Weekly capacity must be between 0 and 168 hours.");
            return member with
            {
                UserId = userId,
                TeamId = Optional(member.TeamId, 128),
                WeeklyCapacityHours = Round(member.WeeklyCapacityHours)
            };
        }).ToList();
        var allocations = NormalizeAllocations(
            request.Allocations
                ?? throw new ValidationException("Capacity-plan allocation list is required."),
            memberIds,
            projectIds.ToHashSet(StringComparer.Ordinal),
            request.PeriodStart,
            request.PeriodEnd);
        return request with
        {
            Name = Required(request.Name, "Capacity-plan name", 120),
            Description = Optional(request.Description, 500),
            PortfolioId = Optional(request.PortfolioId, 128),
            ProjectIds = projectIds,
            Members = members,
            Allocations = allocations,
            ViewerUserIds = viewers
        };
    }
}
