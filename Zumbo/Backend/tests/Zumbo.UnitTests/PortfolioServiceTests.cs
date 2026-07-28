using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class PortfolioServiceTests
{
    [Fact]
    public async Task CreatesHierarchyAndPreservesHealthHistory()
    {
        var fixture = new Fixture();
        var created = await fixture.Service.SaveAsync(
            null,
            Request(),
            "correlation-1",
            CancellationToken.None);

        var parent = await fixture.Service.SaveInitiativeAsync(
            created.Id,
            null,
            new SaveInitiativeRequest(
                "Platform",
                "Shared delivery foundation",
                null,
                "owner-1",
                InitiativeStatuses.Active,
                InitiativeHealth.OnTrack,
                80,
                new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
                ["project-1"],
                []),
            "correlation-2",
            CancellationToken.None);
        var parentId = Assert.Single(parent.Initiatives).Id;

        var child = await fixture.Service.SaveInitiativeAsync(
            created.Id,
            null,
            new SaveInitiativeRequest(
                "Mobile",
                null,
                parentId,
                "owner-1",
                InitiativeStatuses.Active,
                InitiativeHealth.AtRisk,
                55,
                null,
                ["project-2"],
                []),
            "correlation-3",
            CancellationToken.None);
        var childId = child.Initiatives.Single(item => item.Name == "Mobile").Id;

        var updated = await fixture.Service.AddStatusUpdateAsync(
            created.Id,
            childId,
            new AddInitiativeStatusUpdateRequest(
                InitiativeStatuses.Active,
                InitiativeHealth.OffTrack,
                35,
                "External dependency moved."),
            "correlation-4",
            CancellationToken.None);

        var initiative = updated.Initiatives.Single(item => item.Id == childId);
        Assert.Equal(InitiativeHealth.OffTrack, initiative.Health);
        Assert.Equal(35, initiative.Confidence);
        Assert.Equal("External dependency moved.", Assert.Single(initiative.StatusUpdates).Note);
        Assert.Equal(4, updated.Version);
    }

    [Fact]
    public async Task InitiativeHistoryRetainsTheMostRecentFiftyUpdates()
    {
        var fixture = new Fixture();
        var portfolio = await fixture.Service.SaveAsync(
            null,
            Request(),
            "correlation",
            CancellationToken.None);
        portfolio = await fixture.Service.SaveInitiativeAsync(
            portfolio.Id,
            null,
            Initiative("Delivery", null, ["project-1"]),
            "correlation",
            CancellationToken.None);
        var initiativeId = Assert.Single(portfolio.Initiatives).Id;

        for (var index = 1; index <= 51; index++)
        {
            portfolio = await fixture.Service.AddStatusUpdateAsync(
                portfolio.Id,
                initiativeId,
                new AddInitiativeStatusUpdateRequest(
                    InitiativeStatuses.Active,
                    InitiativeHealth.OnTrack,
                    50,
                    $"Status {index}"),
                "correlation",
                CancellationToken.None);
        }

        var updates = Assert.Single(portfolio.Initiatives).StatusUpdates;
        Assert.Equal(50, updates.Count);
        Assert.Equal("Status 51", updates.First().Note);
        Assert.Equal("Status 2", updates.Last().Note);
        Assert.Equal(
            ProjectHistoryRetentionPolicy.MaximumInitiativeStatusUpdates,
            Assert.Single(portfolio.Initiatives).StatusUpdateRetentionLimit);
    }

    [Fact]
    public async Task RejectsHierarchyCyclesAndForeignProjects()
    {
        var fixture = new Fixture();
        var portfolio = await fixture.Service.SaveAsync(
            null,
            Request(),
            "correlation",
            CancellationToken.None);
        portfolio = await fixture.Service.SaveInitiativeAsync(
            portfolio.Id,
            null,
            Initiative("Parent", null, ["project-1"]),
            "correlation",
            CancellationToken.None);
        var parentId = Assert.Single(portfolio.Initiatives).Id;
        portfolio = await fixture.Service.SaveInitiativeAsync(
            portfolio.Id,
            null,
            Initiative("Child", parentId, ["project-2"]),
            "correlation",
            CancellationToken.None);
        var childId = portfolio.Initiatives.Single(item => item.Name == "Child").Id;

        await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.SaveInitiativeAsync(
                portfolio.Id,
                parentId,
                Initiative("Parent", childId, ["project-1"]),
                "correlation",
                CancellationToken.None));

        fixture.Directory.ForeignProjects.Add("foreign-project");
        await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.SaveInitiativeAsync(
                portfolio.Id,
                null,
                Initiative("Foreign", null, ["foreign-project"]),
                "correlation",
                CancellationToken.None));
    }

    [Fact]
    public async Task RoadmapIsPartialAfterSourcePermissionLoss()
    {
        var fixture = new Fixture();
        var portfolio = await fixture.Service.SaveAsync(
            null,
            Request(),
            "correlation",
            CancellationToken.None);
        portfolio = await fixture.Service.SaveInitiativeAsync(
            portfolio.Id,
            null,
            Initiative("Delivery", null, ["project-1", "project-2"]),
            "correlation",
            CancellationToken.None);

        fixture.Directory.DeniedProjects.Add("project-2");
        var roadmap = await fixture.Service.GetRoadmapAsync(
            portfolio.Id,
            CancellationToken.None);

        Assert.Equal(PortfolioSourceStatuses.Partial, roadmap.SourceStatus);
        Assert.Equal(["project-2"], roadmap.UnavailableProjectIds);
        var initiative = Assert.Single(roadmap.Initiatives);
        Assert.Equal(50, initiative.Progress);
        Assert.Equal(1, initiative.CompletedWorkItems);
        Assert.Equal(2, initiative.TotalWorkItems);
    }

    [Fact]
    public async Task StaleExpectedVersionFailsCompareExchange()
    {
        var fixture = new Fixture();
        var created = await fixture.Service.SaveAsync(
            null,
            Request(),
            "correlation",
            CancellationToken.None);
        var stale = await fixture.Repository.SelectAsync(item => item.Id == created.Id);
        _ = await fixture.Service.SaveAsync(
            created.Id,
            Request() with { Name = "Updated portfolio" },
            "correlation",
            CancellationToken.None);

        stale!.Name = "Stale update";
        await Assert.ThrowsAsync<DocumentConcurrencyException>(() =>
            fixture.Repository.ReplaceByVersionAsync(
                item => item.Id == stale.Id,
                stale,
                stale.Version));
    }

    [Fact]
    public async Task InitiativeOwnerCanPublishStatusButCannotEditPortfolio()
    {
        var fixture = new Fixture();
        var portfolio = await fixture.Service.SaveAsync(
            null,
            Request(),
            "correlation",
            CancellationToken.None);
        portfolio = await fixture.Service.SaveInitiativeAsync(
            portfolio.Id,
            null,
            Initiative("Viewer-owned initiative", null, ["project-1"]) with
            {
                OwnerUserId = "viewer-1"
            },
            "correlation",
            CancellationToken.None);
        var initiative = Assert.Single(portfolio.Initiatives);

        fixture.Current.UserId = "viewer-1";
        var visible = await fixture.Service.GetAsync(
            portfolio.Id,
            includeArchived: false,
            CancellationToken.None);
        Assert.False(visible.CanEdit);
        Assert.True(Assert.Single(visible.Initiatives).CanUpdateStatus);

        var updated = await fixture.Service.AddStatusUpdateAsync(
            portfolio.Id,
            initiative.Id,
            new AddInitiativeStatusUpdateRequest(
                InitiativeStatuses.Active,
                InitiativeHealth.AtRisk,
                60,
                "Owner status update."),
            "correlation",
            CancellationToken.None);
        Assert.Single(Assert.Single(updated.Initiatives).StatusUpdates);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            fixture.Service.SaveAsync(
                portfolio.Id,
                Request() with { Name = "Forbidden edit" },
                "correlation",
                CancellationToken.None));
    }

    private static SavePortfolioRequest Request() =>
        new("Delivery portfolio", "Synthetic portfolio", ["viewer-1"]);

    private static SaveInitiativeRequest Initiative(
        string name,
        string? parentId,
        IReadOnlyCollection<string> projectIds) =>
        new(
            name,
            null,
            parentId,
            "owner-1",
            InitiativeStatuses.Active,
            InitiativeHealth.OnTrack,
            75,
            null,
            projectIds,
            []);

    private sealed class Fixture
    {
        public InMemoryDocumentRepository<PortfolioDocument> Repository { get; } = new();
        public PortfolioDirectory Directory { get; } = new();
        public CurrentUser Current { get; } = new();
        public PortfolioService Service { get; }

        public Fixture()
        {
            Service = new PortfolioService(
                Repository,
                Directory,
                new CapturingAudit(),
                Current,
                new FixedClock());
        }
    }

    private sealed class PortfolioDirectory : IPortfolioDirectory
    {
        public HashSet<string> ForeignProjects { get; } = new(StringComparer.Ordinal);
        public HashSet<string> DeniedProjects { get; } = new(StringComparer.Ordinal);

        public Task EnsureOrganizationUsersAsync(
            string organizationId,
            IReadOnlyCollection<string> userIds,
            CancellationToken ct) => Task.CompletedTask;

        public Task EnsureProjectsManageableAsync(
            string organizationId,
            IReadOnlyCollection<string> projectIds,
            CancellationToken ct)
        {
            if (projectIds.Any(ForeignProjects.Contains))
                throw new ValidationException("Portfolio projects must belong to the active organization.");
            return Task.CompletedTask;
        }

        public Task EnsureMilestoneLinksAsync(
            string organizationId,
            IReadOnlyCollection<PortfolioMilestoneLinkRequest> milestoneLinks,
            CancellationToken ct) => Task.CompletedTask;

        public Task<PortfolioProjectSourceResult> ReadProjectSourcesAsync(
            string organizationId,
            IReadOnlyCollection<string> projectIds,
            CancellationToken ct)
        {
            var sources = projectIds
                .Where(projectId => !DeniedProjects.Contains(projectId))
                .Select(projectId => new PortfolioProjectSource(
                    projectId,
                    projectId,
                    "Project " + projectId,
                    2,
                    1,
                    0,
                    [],
                    new DateTimeOffset(2026, 7, 28, 10, 0, 0, TimeSpan.Zero)))
                .ToList();
            return Task.FromResult(new PortfolioProjectSourceResult(
                sources,
                projectIds.Where(DeniedProjects.Contains).ToList()));
        }
    }

    private sealed class CapturingAudit : IPortfolioAuditWriter
    {
        public Task WriteAsync(
            string action,
            string portfolioId,
            string? oldValue,
            string? newValue,
            string correlationId,
            CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class CurrentUser : ICurrentUser
    {
        public string? UserId { get; set; } = "owner-1";
        public string? OrganizationId => "organization-1";
        public IReadOnlyCollection<string> Roles => ["User"];
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);
    }
}
