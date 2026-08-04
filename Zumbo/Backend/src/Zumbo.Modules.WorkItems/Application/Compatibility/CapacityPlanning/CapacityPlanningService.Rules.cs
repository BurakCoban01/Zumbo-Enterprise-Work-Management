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

    private (string UserId, string OrganizationId) CurrentActor()
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException("Active organization is required.");
        return (userId, organizationId);
    }

    private static void EnsureOwner(
        CapacityPlanDocument plan,
        (string UserId, string OrganizationId) actor)
    {
        EnsureVisible(plan, actor);
        if (plan.OwnerUserId != actor.UserId)
            throw new ForbiddenException("Only the capacity-plan owner can change this plan.");
    }

    private static void EnsureVisible(
        CapacityPlanDocument plan,
        (string UserId, string OrganizationId) actor)
    {
        if (plan.OwnerUserId != actor.UserId
            && !plan.ViewerUserIds.Contains(actor.UserId, StringComparer.Ordinal))
        {
            throw new NotFoundException(
                "CAPACITY_PLAN_NOT_FOUND",
                "Capacity plan was not found.");
        }
    }

    private async Task<CapacityPlanDocument> GetDocumentAsync(
        string planId,
        bool includeArchived,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        return await plans.SelectAsync(
            item => item.Id == planId
                && item.OrganizationId == actor.OrganizationId
                && (includeArchived || !item.Archived),
            ct)
            ?? throw new NotFoundException(
                "CAPACITY_PLAN_NOT_FOUND",
                "Capacity plan was not found.");
    }

    private async Task<bool> HasVisibleProjectAsync(
        CapacityPlanDocument plan,
        (string UserId, string OrganizationId) actor,
        CancellationToken ct) =>
        (await directory.ReadProjectAccessAsync(
            actor.OrganizationId,
            actor.UserId,
            plan.ProjectIds,
            ct)).Any(item => item.Available);

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

    private static List<string> NormalizeIds(
        IReadOnlyCollection<string> values,
        int maximum,
        string label)
    {
        var result = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Required(value, label, 128))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (result.Count > maximum)
            throw new ValidationException($"{label} list cannot exceed {maximum} entries.");
        return result;
    }

    private static string? Optional(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maximum)
            throw new ValidationException($"Value cannot exceed {maximum} characters.");
        return normalized;
    }

    private static string Required(string? value, string label, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{label} is required.");
        var normalized = value.Trim();
        if (normalized.Length > maximum)
            throw new ValidationException($"{label} cannot exceed {maximum} characters.");
        return normalized;
    }
}
