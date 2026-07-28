using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record CapacityMemberRequest(
    string UserId,
    string? TeamId,
    decimal WeeklyCapacityHours);

public sealed record CapacityAllocationRequest(
    string? Id,
    string UserId,
    string ProjectId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal Percent);

public sealed record SaveCapacityPlanRequest(
    string Name,
    string? Description,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string? PortfolioId,
    IReadOnlyCollection<string> ProjectIds,
    IReadOnlyCollection<CapacityMemberRequest> Members,
    IReadOnlyCollection<CapacityAllocationRequest> Allocations,
    IReadOnlyCollection<string> ViewerUserIds);

public sealed record ShareCapacityPlanRequest(IReadOnlyCollection<string> ViewerUserIds);

public sealed record CapacityScenarioRequest(
    IReadOnlyCollection<CapacityAllocationRequest> Allocations);

public sealed record CapacityMemberResponse(
    string UserId,
    string? TeamId,
    decimal WeeklyCapacityHours);

public sealed record CapacityAllocationResponse(
    string Id,
    string UserId,
    string ProjectId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal Percent);

public sealed record CapacityPlanResponse(
    string Id,
    string OwnerUserId,
    string Name,
    string? Description,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string? PortfolioId,
    IReadOnlyCollection<string> ProjectIds,
    IReadOnlyCollection<CapacityMemberResponse> Members,
    IReadOnlyCollection<CapacityAllocationResponse> Allocations,
    IReadOnlyCollection<string> ViewerUserIds,
    bool CanEdit,
    bool Archived,
    DateTimeOffset UpdatedAt,
    long Version) : IVersionedResource;

public sealed record CapacityPlanPageResponse(
    IReadOnlyCollection<CapacityPlanResponse> Items,
    int Page,
    int PageSize,
    long Total);

public sealed record CapacityProjectAccess(
    string Id,
    string Key,
    string Name,
    bool Available);

public sealed record CapacityTaskResponse(
    string Id,
    string ProjectId,
    string Title,
    string? AssigneeUserId,
    DateOnly? DueDate,
    decimal? EstimatePoints);

public sealed record CapacityWeekResponse(
    DateOnly WeekStart,
    decimal CapacityHours,
    decimal AllocatedHours,
    decimal RemainingHours,
    int AllocationPercent,
    string State,
    decimal EstimatedPoints,
    int UnestimatedItems,
    int ScheduledItems);

public sealed record CapacityMemberSnapshotResponse(
    string UserId,
    string? TeamId,
    decimal WeeklyCapacityHours,
    decimal CapacityHours,
    decimal AllocatedHours,
    decimal RemainingHours,
    int AllocationPercent,
    string State,
    decimal EstimatedPoints,
    int UnestimatedItems,
    int UnscheduledItems,
    int OpenItems,
    IReadOnlyCollection<CapacityWeekResponse> Weeks,
    IReadOnlyCollection<CapacityTaskResponse> Tasks);

public sealed record CapacityTeamSnapshotResponse(
    string TeamId,
    int Members,
    decimal CapacityHours,
    decimal AllocatedHours,
    decimal RemainingHours,
    string State,
    int OpenItems,
    int UnestimatedItems);

public sealed record CapacityProjectSnapshotResponse(
    string ProjectId,
    string Key,
    string Name,
    int AllocatedPeople,
    decimal AllocatedHours,
    int OpenItems,
    decimal EstimatedPoints,
    int UnestimatedItems);

public sealed record CapacitySnapshotSummaryResponse(
    int People,
    decimal CapacityHours,
    decimal AllocatedHours,
    decimal RemainingHours,
    int OverCapacityPeople,
    int OpenItems,
    decimal EstimatedPoints,
    int UnestimatedItems,
    int UnscheduledItems);

public sealed record CapacitySnapshotResponse(
    string PlanId,
    long PlanVersion,
    string SourceStatus,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTimeOffset GeneratedAt,
    bool Truncated,
    IReadOnlyCollection<string> UnavailableProjectIds,
    CapacitySnapshotSummaryResponse Summary,
    IReadOnlyCollection<CapacityMemberSnapshotResponse> Members,
    IReadOnlyCollection<CapacityTeamSnapshotResponse> Teams,
    IReadOnlyCollection<CapacityProjectSnapshotResponse> Projects);

public sealed record CapacityScenarioResponse(
    string PlanId,
    long PlanVersion,
    CapacitySnapshotResponse Baseline,
    CapacitySnapshotResponse Candidate);

public interface ICapacityPlanningDirectory
{
    Task EnsureOrganizationUsersAndTeamsAsync(
        string organizationId,
        IReadOnlyCollection<CapacityMemberRequest> members,
        IReadOnlyCollection<string> viewerUserIds,
        CancellationToken ct);

    Task EnsureManageableScopeAsync(
        string organizationId,
        string actorUserId,
        string? portfolioId,
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct);

    Task<IReadOnlyCollection<CapacityProjectAccess>> ReadProjectAccessAsync(
        string organizationId,
        string actorUserId,
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct);
}

public interface ICapacityPlanningAuditWriter
{
    Task WriteAsync(
        string action,
        string planId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct);
}

public sealed class CapacityPlanningService(
    IDocumentRepository<CapacityPlanDocument> plans,
    IDocumentRepository<WorkItemDocument> workItems,
    ICapacityPlanningDirectory directory,
    ICapacityPlanningAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private const int MaximumProjects = 20;
    private const int MaximumMembers = 100;
    private const int MaximumAllocations = 500;
    private const int MaximumViewers = 50;
    private const int MaximumSourceItems = 10_000;
    private const int SourcePageSize = 500;
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    public async Task<CapacityPlanResponse> SaveAsync(
        string? planId,
        SaveCapacityPlanRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        CapacityPlanDocument? existing = null;
        if (planId is not null)
        {
            existing = await GetDocumentAsync(planId, includeArchived: false, ct);
            EnsureOwner(existing, actor);
        }

        var definition = Normalize(request);
        await directory.EnsureOrganizationUsersAndTeamsAsync(
            actor.OrganizationId,
            definition.Members,
            definition.ViewerUserIds,
            ct);
        await directory.EnsureManageableScopeAsync(
            actor.OrganizationId,
            actor.UserId,
            definition.PortfolioId,
            definition.ProjectIds,
            ct);

        CapacityPlanDocument plan;
        string action;
        string? oldValue;
        if (planId is null)
        {
            var now = clock.UtcNow;
            plan = new CapacityPlanDocument
            {
                OrganizationId = actor.OrganizationId,
                OwnerUserId = actor.UserId,
                CreatedAt = now,
                UpdatedAt = now
            };
            Apply(plan, definition, now);
            plan = await plans.CreateAsync(plan, ct);
            action = "CapacityPlanCreated";
            oldValue = null;
        }
        else
        {
            plan = existing!;
            oldValue = plan.Name;
            Apply(plan, definition, clock.UtcNow);
            await ReplaceAsync(plan, ct);
            action = "CapacityPlanUpdated";
        }

        await audit.WriteAsync(action, plan.Id, oldValue, plan.Name, correlationId, ct);
        return ToResponse(plan, actor.UserId);
    }

    public async Task<CapacityPlanResponse> ShareAsync(
        string planId,
        ShareCapacityPlanRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var plan = await GetDocumentAsync(planId, includeArchived: false, ct);
        EnsureOwner(plan, actor);
        var viewers = NormalizeIds(
            request.ViewerUserIds
                ?? throw new ValidationException("Capacity-plan viewer list is required."),
            MaximumViewers,
            "Capacity-plan viewer");
        if (viewers.Contains(actor.UserId, StringComparer.Ordinal))
            throw new ValidationException("Capacity-plan owner cannot also be a viewer.");
        await directory.EnsureOrganizationUsersAndTeamsAsync(
            actor.OrganizationId,
            [],
            viewers,
            ct);
        var oldValue = string.Join(",", plan.ViewerUserIds.Order(StringComparer.Ordinal));
        plan.ViewerUserIds = viewers;
        plan.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(plan, ct);
        await audit.WriteAsync(
            "CapacityPlanSharingUpdated",
            plan.Id,
            oldValue,
            string.Join(",", viewers.Order(StringComparer.Ordinal)),
            correlationId,
            ct);
        return ToResponse(plan, actor.UserId);
    }

    public async Task<CapacityPlanPageResponse> ListAsync(
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 100);
        var candidates = await plans.ListByFilterAsync(
            item => item.OrganizationId == actor.OrganizationId
                && (includeArchived || !item.Archived)
                && (item.OwnerUserId == actor.UserId
                    || item.ViewerUserIds.Contains(actor.UserId)),
            item => item.UpdatedAt,
            orderDescending: true,
            page: 1,
            pageSize: 500,
            cancellationToken: ct);
        var visible = new List<CapacityPlanDocument>();
        foreach (var plan in candidates)
        {
            if (plan.OwnerUserId == actor.UserId
                || await HasVisibleProjectAsync(plan, actor, ct))
            {
                visible.Add(plan);
            }
        }
        return new CapacityPlanPageResponse(
            visible
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .Select(item => ToResponse(item, actor.UserId))
                .ToList(),
            normalizedPage,
            normalizedPageSize,
            visible.Count);
    }

    public async Task<CapacityPlanResponse> GetAsync(
        string planId,
        bool includeArchived,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var plan = await GetDocumentAsync(planId, includeArchived, ct);
        EnsureVisible(plan, actor);
        if (plan.OwnerUserId != actor.UserId
            && !await HasVisibleProjectAsync(plan, actor, ct))
        {
            throw new NotFoundException(
                "CAPACITY_PLAN_NOT_FOUND",
                "Capacity plan was not found.");
        }
        return ToResponse(plan, actor.UserId);
    }

    public async Task ArchiveAsync(
        string planId,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var plan = await GetDocumentAsync(planId, includeArchived: false, ct);
        EnsureOwner(plan, actor);
        plan.Archived = true;
        plan.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(plan, ct);
        await audit.WriteAsync(
            "CapacityPlanArchived",
            plan.Id,
            plan.Name,
            null,
            correlationId,
            ct);
    }

    public async Task<CapacitySnapshotResponse> GetSnapshotAsync(
        string planId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var plan = await GetDocumentAsync(planId, includeArchived: false, ct);
        EnsureVisible(plan, actor);
        var source = await LoadSourceAsync(plan, actor, ct);
        return BuildSnapshot(plan, plan.Allocations, source);
    }

    public async Task<CapacityScenarioResponse> PreviewScenarioAsync(
        string planId,
        CapacityScenarioRequest request,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var plan = await GetDocumentAsync(planId, includeArchived: false, ct);
        EnsureOwner(plan, actor);
        var allocations = NormalizeAllocations(
            request.Allocations
                ?? throw new ValidationException("Scenario allocations are required."),
            plan.Members.Select(item => item.UserId).ToHashSet(StringComparer.Ordinal),
            plan.ProjectIds.ToHashSet(StringComparer.Ordinal),
            DateOnlyUtc(plan.PeriodStartUtc),
            DateOnlyUtc(plan.PeriodEndUtc));
        var source = await LoadSourceAsync(plan, actor, ct);
        return new CapacityScenarioResponse(
            plan.Id,
            plan.Version,
            BuildSnapshot(plan, plan.Allocations, source),
            BuildSnapshot(plan, allocations.Select(ToDocument).ToList(), source));
    }

    private async Task<CapacitySource> LoadSourceAsync(
        CapacityPlanDocument plan,
        (string UserId, string OrganizationId) actor,
        CancellationToken ct)
    {
        var access = await directory.ReadProjectAccessAsync(
            actor.OrganizationId,
            actor.UserId,
            plan.ProjectIds,
            ct);
        var visible = access.Where(item => item.Available).ToList();
        if (plan.OwnerUserId != actor.UserId && visible.Count == 0)
        {
            throw new NotFoundException(
                "CAPACITY_PLAN_NOT_FOUND",
                "Capacity plan was not found.");
        }

        var loaded = new List<WorkItemDocument>();
        long sourceCount = 0;
        foreach (var project in visible)
        {
            sourceCount += await workItems.CountByFilterAsync(
                item => item.ProjectId == project.Id
                    && !item.Archived
                    && item.CompletedAt == null
                    && item.Status != "Done",
                ct);
        }
        var truncated = sourceCount > MaximumSourceItems;
        foreach (var project in visible)
        {
            string? cursor = null;
            do
            {
                var remaining = MaximumSourceItems - loaded.Count;
                if (remaining <= 0)
                {
                    break;
                }
                var result = await workItems.ListByCursorAsync(
                    item => item.ProjectId == project.Id
                        && !item.Archived
                        && item.CompletedAt == null
                        && item.Status != "Done",
                    cursor,
                    Math.Min(SourcePageSize, remaining),
                    ct);
                loaded.AddRange(result.Items);
                cursor = result.NextCursor;
            } while (cursor is not null);
            if (loaded.Count == MaximumSourceItems) break;
        }
        return new CapacitySource(
            visible,
            access.Where(item => !item.Available).Select(item => item.Id).ToList(),
            loaded,
            truncated);
    }

    private CapacitySnapshotResponse BuildSnapshot(
        CapacityPlanDocument plan,
        IReadOnlyCollection<CapacityAllocationDocument> allocations,
        CapacitySource source)
    {
        var start = DateOnlyUtc(plan.PeriodStartUtc);
        var end = DateOnlyUtc(plan.PeriodEndUtc);
        var weekStarts = Weeks(start, end);
        var memberRows = plan.Members.Select(member =>
        {
            var memberAllocations = allocations
                .Where(item => item.UserId == member.UserId)
                .ToList();
            var memberTasks = source.Tasks
                .Where(item => item.AssigneeUserId == member.UserId)
                .Select(ToTask)
                .ToList();
            var weeks = weekStarts.Select(weekStart =>
            {
                var weekEnd = weekStart.AddDays(6);
                var capacity = Round(member.WeeklyCapacityHours
                    * WorkingDays(Max(start, weekStart), Min(end, weekEnd)) / 5m);
                var allocated = Round(memberAllocations.Sum(allocation =>
                {
                    var allocationStart = DateOnlyUtc(allocation.StartDateUtc);
                    var allocationEnd = DateOnlyUtc(allocation.EndDateUtc);
                    var overlapStart = Max(Max(start, weekStart), allocationStart);
                    var overlapEnd = Min(Min(end, weekEnd), allocationEnd);
                    if (overlapEnd < overlapStart) return 0m;
                    return member.WeeklyCapacityHours
                        * WorkingDays(overlapStart, overlapEnd) / 5m
                        * allocation.Percent / 100m;
                }));
                var dueTasks = memberTasks
                    .Where(item => item.DueDate >= weekStart && item.DueDate <= weekEnd)
                    .ToList();
                return new CapacityWeekResponse(
                    weekStart,
                    capacity,
                    allocated,
                    Round(capacity - allocated),
                    Percent(allocated, capacity),
                    State(allocated, capacity),
                    Round(dueTasks.Sum(item => item.EstimatePoints ?? 0)),
                    dueTasks.Count(item => item.EstimatePoints is null),
                    dueTasks.Count);
            }).ToList();
            var capacityHours = Round(weeks.Sum(item => item.CapacityHours));
            var allocatedHours = Round(weeks.Sum(item => item.AllocatedHours));
            return new CapacityMemberSnapshotResponse(
                member.UserId,
                member.TeamId,
                member.WeeklyCapacityHours,
                capacityHours,
                allocatedHours,
                Round(capacityHours - allocatedHours),
                Percent(allocatedHours, capacityHours),
                State(allocatedHours, capacityHours),
                Round(memberTasks.Sum(item => item.EstimatePoints ?? 0)),
                memberTasks.Count(item => item.EstimatePoints is null),
                memberTasks.Count(item => item.DueDate is null),
                memberTasks.Count,
                weeks,
                memberTasks);
        }).ToList();

        var teamRows = memberRows
            .Where(item => item.TeamId is not null)
            .GroupBy(item => item.TeamId!, StringComparer.Ordinal)
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var capacity = Round(group.Sum(item => item.CapacityHours));
                var allocated = Round(group.Sum(item => item.AllocatedHours));
                return new CapacityTeamSnapshotResponse(
                    group.Key,
                    group.Count(),
                    capacity,
                    allocated,
                    Round(capacity - allocated),
                    State(allocated, capacity),
                    group.Sum(item => item.OpenItems),
                    group.Sum(item => item.UnestimatedItems));
            }).ToList();

        var projectRows = source.Projects.Select(project =>
        {
            var projectAllocations = allocations
                .Where(item => item.ProjectId == project.Id)
                .ToList();
            var projectTasks = source.Tasks
                .Where(item => item.ProjectId == project.Id)
                .ToList();
            var allocatedHours = memberRows.Sum(member =>
                member.Weeks.Sum(week =>
                {
                    var memberAllocation = projectAllocations
                        .Where(item => item.UserId == member.UserId)
                        .Sum(item =>
                        {
                            var weekEnd = week.WeekStart.AddDays(6);
                            var allocationStart = DateOnlyUtc(item.StartDateUtc);
                            var allocationEnd = DateOnlyUtc(item.EndDateUtc);
                            var overlapStart = Max(Max(start, week.WeekStart), allocationStart);
                            var overlapEnd = Min(Min(end, weekEnd), allocationEnd);
                            if (overlapEnd < overlapStart) return 0m;
                            return member.WeeklyCapacityHours
                                * WorkingDays(overlapStart, overlapEnd) / 5m
                                * item.Percent / 100m;
                        });
                    return memberAllocation;
                }));
            return new CapacityProjectSnapshotResponse(
                project.Id,
                project.Key,
                project.Name,
                projectAllocations.Select(item => item.UserId).Distinct(StringComparer.Ordinal).Count(),
                Round(allocatedHours),
                projectTasks.Count,
                Round(projectTasks.Sum(item => item.EstimatePoints)),
                projectTasks.Count(item => item.EstimatePoints <= 0));
        }).ToList();

        var totalCapacity = Round(memberRows.Sum(item => item.CapacityHours));
        var totalAllocated = Round(memberRows.Sum(item => item.AllocatedHours));
        return new CapacitySnapshotResponse(
            plan.Id,
            plan.Version,
            source.UnavailableProjectIds.Count > 0 || source.Truncated
                ? CapacitySnapshotStatuses.Partial
                : CapacitySnapshotStatuses.Ready,
            start,
            end,
            clock.UtcNow,
            source.Truncated,
            source.UnavailableProjectIds,
            new CapacitySnapshotSummaryResponse(
                memberRows.Count,
                totalCapacity,
                totalAllocated,
                Round(totalCapacity - totalAllocated),
                memberRows.Count(item => item.State == CapacityLoadStates.OverCapacity),
                source.Tasks.Count,
                Round(source.Tasks.Sum(item => item.EstimatePoints)),
                source.Tasks.Count(item => item.EstimatePoints <= 0),
                source.Tasks.Count(item => item.DueDate is null)),
            memberRows,
            teamRows,
            projectRows);
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

    private async Task ReplaceAsync(CapacityPlanDocument plan, CancellationToken ct)
    {
        var result = await plans.ReplaceByVersionAsync(
            item => item.Id == plan.Id && item.OrganizationId == plan.OrganizationId,
            plan,
            expectedVersion.Consume(plan.Version),
            ct);
        if (!result.Found)
            throw new NotFoundException("CAPACITY_PLAN_NOT_FOUND", "Capacity plan was not found.");
        plan.Version = result.Version!.Value;
    }

    private (string UserId, string OrganizationId) CurrentActor()
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException("Active organization is required.");
        return (userId, organizationId);
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

    private static void EnsureOwner(
        CapacityPlanDocument plan,
        (string UserId, string OrganizationId) actor)
    {
        EnsureVisible(plan, actor);
        if (plan.OwnerUserId != actor.UserId)
            throw new ForbiddenException("Only the capacity-plan owner can change this plan.");
    }

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

    private static void Apply(
        CapacityPlanDocument plan,
        SaveCapacityPlanRequest request,
        DateTimeOffset now)
    {
        plan.Name = request.Name;
        plan.Description = request.Description;
        plan.PeriodStartUtc = UtcDay(request.PeriodStart);
        plan.PeriodEndUtc = UtcDay(request.PeriodEnd);
        plan.PortfolioId = request.PortfolioId;
        plan.ProjectIds = request.ProjectIds.ToList();
        plan.Members = request.Members.Select(item => new CapacityMemberDocument
        {
            UserId = item.UserId,
            TeamId = item.TeamId,
            WeeklyCapacityHours = item.WeeklyCapacityHours
        }).ToList();
        plan.Allocations = request.Allocations.Select(ToDocument).ToList();
        plan.ViewerUserIds = request.ViewerUserIds.ToList();
        plan.UpdatedAt = now;
    }

    private static CapacityAllocationDocument ToDocument(CapacityAllocationRequest item) => new()
    {
        Id = item.Id!,
        UserId = item.UserId,
        ProjectId = item.ProjectId,
        StartDateUtc = UtcDay(item.StartDate),
        EndDateUtc = UtcDay(item.EndDate),
        Percent = item.Percent
    };

    private static CapacityPlanResponse ToResponse(CapacityPlanDocument plan, string userId) => new(
        plan.Id,
        plan.OwnerUserId,
        plan.Name,
        plan.Description,
        DateOnlyUtc(plan.PeriodStartUtc),
        DateOnlyUtc(plan.PeriodEndUtc),
        plan.PortfolioId,
        plan.ProjectIds,
        plan.Members.Select(item => new CapacityMemberResponse(
            item.UserId,
            item.TeamId,
            item.WeeklyCapacityHours)).ToList(),
        plan.Allocations.Select(item => new CapacityAllocationResponse(
            item.Id,
            item.UserId,
            item.ProjectId,
            DateOnlyUtc(item.StartDateUtc),
            DateOnlyUtc(item.EndDateUtc),
            item.Percent)).ToList(),
        plan.ViewerUserIds,
        plan.OwnerUserId == userId,
        plan.Archived,
        plan.UpdatedAt,
        plan.Version);

    private static CapacityTaskResponse ToTask(WorkItemDocument item) => new(
        item.Id,
        item.ProjectId,
        item.Title,
        item.AssigneeUserId,
        item.DueDate is null ? null : DateOnlyUtc(item.DueDate.Value),
        item.EstimatePoints <= 0 ? null : item.EstimatePoints);

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

    private static int WorkingDays(DateOnly start, DateOnly end)
    {
        if (end < start) return 0;
        var count = 0;
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                count++;
        }
        return count;
    }

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

    private static int Percent(decimal allocated, decimal capacity) =>
        capacity <= 0
            ? allocated > 0 ? 100 : 0
            : (int)Math.Round(allocated / capacity * 100m, MidpointRounding.AwayFromZero);

    private static DateOnly Max(DateOnly left, DateOnly right) => left > right ? left : right;
    private static DateOnly Min(DateOnly left, DateOnly right) => left < right ? left : right;
    private static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
    private static DateTimeOffset UtcDay(DateOnly value) =>
        new(value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    private static DateOnly DateOnlyUtc(DateTimeOffset value) =>
        DateOnly.FromDateTime(value.UtcDateTime);

    private static string Required(string? value, string label, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{label} is required.");
        var normalized = value.Trim();
        if (normalized.Length > maximum)
            throw new ValidationException($"{label} cannot exceed {maximum} characters.");
        return normalized;
    }

    private static string? Optional(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maximum)
            throw new ValidationException($"Value cannot exceed {maximum} characters.");
        return normalized;
    }

    private sealed record CapacitySource(
        IReadOnlyCollection<CapacityProjectAccess> Projects,
        IReadOnlyCollection<string> UnavailableProjectIds,
        IReadOnlyCollection<WorkItemDocument> Tasks,
        bool Truncated);
}
