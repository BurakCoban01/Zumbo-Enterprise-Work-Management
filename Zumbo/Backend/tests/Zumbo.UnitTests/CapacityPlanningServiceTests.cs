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
    public async Task ViewerWithoutVisibleProjectCannotReadPlan()
    {
        var fixture = new Fixture();
        var plan = await fixture.Service.SaveAsync(
            null,
            Request(),
            "create-capacity-plan",
            CancellationToken.None);
        fixture.Current.UserIdValue = "viewer-1";
        fixture.Directory.ProjectsAvailable = false;

        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.Service.GetAsync(plan.Id, false, CancellationToken.None));

        fixture.Current.UserIdValue = "owner-1";
        Assert.True((await fixture.Service.GetAsync(
            plan.Id,
            false,
            CancellationToken.None)).CanEdit);
    }

    [Fact]
    public async Task ListNormalizesPagingAndFiltersViewerProjectAccess()
    {
        var fixture = new Fixture();
        await fixture.Service.SaveAsync(
            null,
            Request(),
            "create-first-plan",
            CancellationToken.None);
        await fixture.Service.SaveAsync(
            null,
            Request() with { Name = "Second plan" },
            "create-second-plan",
            CancellationToken.None);
        fixture.Current.UserIdValue = "viewer-1";
        fixture.Directory.ProjectsAvailable = false;

        Assert.Empty((await fixture.Service.ListAsync(
            false,
            1,
            50,
            CancellationToken.None)).Items);

        fixture.Directory.ProjectsAvailable = true;
        var normalized = await fixture.Service.ListAsync(
            false,
            0,
            500,
            CancellationToken.None);
        Assert.Equal(1, normalized.Page);
        Assert.Equal(100, normalized.PageSize);
        Assert.Equal(2, normalized.Total);
        Assert.Equal(2, normalized.Items.Count);
        Assert.All(normalized.Items, item => Assert.False(item.CanEdit));

        var secondPage = await fixture.Service.ListAsync(
            false,
            2,
            1,
            CancellationToken.None);
        Assert.Equal(2, secondPage.Page);
        Assert.Equal(1, secondPage.PageSize);
        Assert.Equal(2, secondPage.Total);
        Assert.Single(secondPage.Items);
    }

    [Fact]
    public async Task SharingNormalizesViewersRequiresOwnerAndWritesAudit()
    {
        var fixture = new Fixture();
        var plan = await fixture.Service.SaveAsync(
            null,
            Request(),
            "create-capacity-plan",
            CancellationToken.None);

        await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.ShareAsync(
                plan.Id,
                new ShareCapacityPlanRequest(["owner-1"]),
                "owner-as-viewer",
                CancellationToken.None));

        fixture.Current.UserIdValue = "viewer-1";
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            fixture.Service.ShareAsync(
                plan.Id,
                new ShareCapacityPlanRequest(["viewer-2"]),
                "viewer-share",
                CancellationToken.None));

        fixture.Current.UserIdValue = "owner-1";
        var shared = await fixture.Service.ShareAsync(
            plan.Id,
            new ShareCapacityPlanRequest(
                [" viewer-2 ", "viewer-2", "viewer-3"]),
            "owner-share",
            CancellationToken.None);

        Assert.Equal(["viewer-2", "viewer-3"], shared.ViewerUserIds);
        Assert.Contains(
            fixture.Audit.Entries,
            entry => entry.Action == "CapacityPlanSharingUpdated"
                && entry.PlanId == plan.Id
                && entry.CorrelationId == "owner-share");
    }

    [Fact]
    public async Task SaveCreatesThenUpdatesPlanAndWritesLifecycleAudit()
    {
        var fixture = new Fixture();

        var created = await fixture.Service.SaveAsync(
            null,
            Request() with { Name = "  Quarterly staffing  " },
            "create-plan",
            CancellationToken.None);
        var updated = await fixture.Service.SaveAsync(
            created.Id,
            Request() with { Name = "Updated staffing" },
            "update-plan",
            CancellationToken.None);

        Assert.Equal("Quarterly staffing", created.Name);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("Updated staffing", updated.Name);
        Assert.True(updated.Version > created.Version);
        Assert.Contains(
            fixture.Audit.Entries,
            entry => entry.Action == "CapacityPlanCreated"
                && entry.PlanId == created.Id
                && entry.CorrelationId == "create-plan");
        Assert.Contains(
            fixture.Audit.Entries,
            entry => entry.Action == "CapacityPlanUpdated"
                && entry.PlanId == created.Id
                && entry.CorrelationId == "update-plan");
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

    [Fact]
    public async Task ArchiveRequiresOwnerHidesPlanAndWritesAudit()
    {
        var fixture = new Fixture();
        var plan = await fixture.Service.SaveAsync(
            null,
            Request(),
            "create-capacity-plan",
            CancellationToken.None);
        fixture.Current.UserIdValue = "viewer-1";

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            fixture.Service.ArchiveAsync(
                plan.Id,
                "viewer-archive",
                CancellationToken.None));

        fixture.Current.UserIdValue = "owner-1";
        await fixture.Service.ArchiveAsync(
            plan.Id,
            "owner-archive",
            CancellationToken.None);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.Service.GetAsync(plan.Id, false, CancellationToken.None));
        Assert.True((await fixture.Service.GetAsync(
            plan.Id,
            true,
            CancellationToken.None)).Archived);
        Assert.Contains(
            fixture.Audit.Entries,
            entry => entry.Action == "CapacityPlanArchived"
                && entry.PlanId == plan.Id
                && entry.CorrelationId == "owner-archive");
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
        public Audit Audit { get; } = new();
        public Directory Directory { get; } = new();
        public CapacityPlanningService Service { get; }

        public Fixture()
        {
            Service = new CapacityPlanningService(
                Plans,
                WorkItems,
                Directory,
                Audit,
                Current,
                new Clock());
        }
    }

    private sealed class Directory : ICapacityPlanningDirectory
    {
        public bool ProjectsAvailable { get; set; } = true;

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
                projectIds.Select(id => new CapacityProjectAccess(
                    id,
                    id,
                    id,
                    ProjectsAvailable)).ToList());
    }

    private sealed class Audit : ICapacityPlanningAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task WriteAsync(
            string action,
            string planId,
            string? oldValue,
            string? newValue,
            string correlationId,
            CancellationToken ct)
        {
            Entries.Add(new AuditEntry(action, planId, correlationId));
            return Task.CompletedTask;
        }
    }

    private sealed record AuditEntry(
        string Action,
        string PlanId,
        string CorrelationId);

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
