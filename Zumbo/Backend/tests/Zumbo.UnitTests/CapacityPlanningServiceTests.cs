using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class CapacityPlanningServiceTests
{
    [Fact]
    public async Task SnapshotSeparatesCapacityHoursEstimatePointsAndUnestimatedWork()
    {
        var fixture = new Fixture();
        await fixture.WorkItems.CreateAsync(WorkItem("estimated", 5, new DateTimeOffset(2026, 7, 8, 0, 0, 0, TimeSpan.Zero)));
        await fixture.WorkItems.CreateAsync(WorkItem("unestimated", 0, null));
        var plan = await fixture.Service.SaveAsync(
            null,
            Request(),
            "correlation",
            CancellationToken.None);

        var snapshot = await fixture.Service.GetSnapshotAsync(plan.Id, CancellationToken.None);

        Assert.Equal(CapacitySnapshotStatuses.Ready, snapshot.SourceStatus);
        Assert.Equal(80, snapshot.Summary.CapacityHours);
        Assert.Equal(48, snapshot.Summary.AllocatedHours);
        Assert.Equal(5, snapshot.Summary.EstimatedPoints);
        Assert.Equal(1, snapshot.Summary.UnestimatedItems);
        Assert.Equal(1, snapshot.Summary.UnscheduledItems);
        var member = Assert.Single(snapshot.Members);
        Assert.Equal(CapacityLoadStates.Available, member.State);
        Assert.Equal(60, member.AllocationPercent);
        Assert.Equal(2, member.Weeks.Count);
        Assert.Equal(2, member.Tasks.Count);
        Assert.Single(snapshot.Teams);
        Assert.Equal(2, snapshot.Projects.Count);
        var unallocatedProject = Assert.Single(snapshot.Projects, item => item.ProjectId == "project-2");
        Assert.Equal(0, unallocatedProject.AllocatedHours);
        Assert.Equal(0, unallocatedProject.OpenItems);
    }

    [Fact]
    public async Task ScenarioComparesOverCapacityWithoutPersistingCandidate()
    {
        var fixture = new Fixture();
        var plan = await fixture.Service.SaveAsync(
            null,
            Request(),
            "correlation",
            CancellationToken.None);
        var candidate = Request().Allocations.Concat(
        [
            new CapacityAllocationRequest(
                null,
                "owner-1",
                "project-2",
                new DateOnly(2026, 7, 6),
                new DateOnly(2026, 7, 19),
                50)
        ]).ToList();

        var scenario = await fixture.Service.PreviewScenarioAsync(
            plan.Id,
            new CapacityScenarioRequest(candidate),
            CancellationToken.None);

        Assert.Equal(48, scenario.Baseline.Summary.AllocatedHours);
        Assert.Equal(88, scenario.Candidate.Summary.AllocatedHours);
        Assert.Equal(
            CapacityLoadStates.OverCapacity,
            Assert.Single(scenario.Candidate.Members).State);
        var reloaded = await fixture.Service.GetAsync(plan.Id, false, CancellationToken.None);
        Assert.Single(reloaded.Allocations);
    }

    [Fact]
    public async Task ViewerCanReadButCannotEditOrPreviewAndTenantIsHidden()
    {
        var fixture = new Fixture();
        var plan = await fixture.Service.SaveAsync(
            null,
            Request(),
            "correlation",
            CancellationToken.None);
        fixture.Current.UserIdValue = "viewer-1";

        Assert.False((await fixture.Service.GetAsync(
            plan.Id,
            false,
            CancellationToken.None)).CanEdit);
        Assert.Equal(
            CapacitySnapshotStatuses.Ready,
            (await fixture.Service.GetSnapshotAsync(plan.Id, CancellationToken.None)).SourceStatus);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            fixture.Service.PreviewScenarioAsync(
                plan.Id,
                new CapacityScenarioRequest(Request().Allocations),
                CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            fixture.Service.SaveAsync(
                plan.Id,
                Request() with { Name = "Forbidden" },
                "correlation",
                CancellationToken.None));

        fixture.Current.OrganizationIdValue = "foreign";
        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.Service.GetAsync(plan.Id, false, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsInvalidPeriodAllocationAndMemberBounds()
    {
        var fixture = new Fixture();
        await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.SaveAsync(
                null,
                Request() with { PeriodEnd = new DateOnly(2027, 8, 1) },
                "correlation",
                CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.SaveAsync(
                null,
                Request() with
                {
                    Allocations =
                    [
                        new CapacityAllocationRequest(
                            null,
                            "owner-1",
                            "project-1",
                            new DateOnly(2026, 7, 6),
                            new DateOnly(2026, 7, 19),
                            101)
                    ]
                },
                "correlation",
                CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.SaveAsync(
                null,
                Request() with
                {
                    Members = [new CapacityMemberRequest("owner-1", "team-1", 169)]
                },
                "correlation",
                CancellationToken.None));
    }

    private static SaveCapacityPlanRequest Request() => new(
        "Quarterly staffing",
        "Synthetic capacity plan",
        new DateOnly(2026, 7, 6),
        new DateOnly(2026, 7, 19),
        null,
        ["project-1", "project-2"],
        [new CapacityMemberRequest("owner-1", "team-1", 40)],
        [
            new CapacityAllocationRequest(
                null,
                "owner-1",
                "project-1",
                new DateOnly(2026, 7, 6),
                new DateOnly(2026, 7, 19),
                60)
        ],
        ["viewer-1"]);

    private static WorkItemDocument WorkItem(
        string id,
        decimal estimate,
        DateTimeOffset? dueDate) => new()
    {
        Id = id,
        ProjectId = "project-1",
        BoardId = "board-1",
        ColumnId = "column-1",
        Title = id,
        Status = "In Progress",
        AssigneeUserId = "owner-1",
        EstimatePoints = estimate,
        DueDate = dueDate,
        CreatedAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)
    };

    private sealed class Fixture
    {
        public InMemoryDocumentRepository<CapacityPlanDocument> Plans { get; } = new();
        public InMemoryDocumentRepository<WorkItemDocument> WorkItems { get; } = new();
        public CurrentUser Current { get; } = new();
        public CapacityPlanningService Service { get; }

        public Fixture()
        {
            Service = new CapacityPlanningService(
                Plans,
                WorkItems,
                new Directory(),
                new Audit(),
                Current,
                new Clock());
        }
    }

    private sealed class Directory : ICapacityPlanningDirectory
    {
        public Task EnsureOrganizationUsersAndTeamsAsync(
            string organizationId,
            IReadOnlyCollection<CapacityMemberRequest> members,
            IReadOnlyCollection<string> viewerUserIds,
            CancellationToken ct) => Task.CompletedTask;

        public Task EnsureManageableScopeAsync(
            string organizationId,
            string actorUserId,
            string? portfolioId,
            IReadOnlyCollection<string> projectIds,
            CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyCollection<CapacityProjectAccess>> ReadProjectAccessAsync(
            string organizationId,
            string actorUserId,
            IReadOnlyCollection<string> projectIds,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<CapacityProjectAccess>>(
                projectIds.Select(id => new CapacityProjectAccess(id, id, id, true)).ToList());
    }

    private sealed class Audit : ICapacityPlanningAuditWriter
    {
        public Task WriteAsync(
            string action,
            string planId,
            string? oldValue,
            string? newValue,
            string correlationId,
            CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class CurrentUser : ICurrentUser
    {
        public string UserIdValue { get; set; } = "owner-1";
        public string OrganizationIdValue { get; set; } = "organization-1";
        public string? UserId => UserIdValue;
        public string? OrganizationId => OrganizationIdValue;
        public IReadOnlyCollection<string> Roles => ["User"];
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow =>
            new(2026, 7, 10, 10, 0, 0, TimeSpan.Zero);
    }
}
