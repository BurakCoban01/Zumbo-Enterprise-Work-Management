using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class GoalServiceTests
{
    [Fact]
    public async Task PreservesExplicitGoalAndKeyResultHistoryWithDirectionAwareProgress()
    {
        var fixture = new Fixture();
        var goal = await fixture.Service.SaveAsync(
            null,
            Request(),
            "correlation-1",
            CancellationToken.None);
        goal = await fixture.Service.SaveKeyResultAsync(
            goal.Id,
            null,
            KeyResult("Adoption", "owner-1", 0, 100, 10, KeyResultDirections.Increase),
            "correlation-2",
            CancellationToken.None);
        var adoption = Assert.Single(goal.KeyResults);
        goal = await fixture.Service.AddKeyResultProgressAsync(
            goal.Id,
            adoption.Id,
            new AddKeyResultProgressRequest(40, 70, "Adoption reached forty percent."),
            "correlation-3",
            CancellationToken.None);
        goal = await fixture.Service.SaveKeyResultAsync(
            goal.Id,
            null,
            KeyResult("Lead time", "owner-1", 10, 2, 10, KeyResultDirections.Decrease),
            "correlation-4",
            CancellationToken.None);
        var leadTime = goal.KeyResults.Single(item => item.Name == "Lead time");
        goal = await fixture.Service.AddKeyResultProgressAsync(
            goal.Id,
            leadTime.Id,
            new AddKeyResultProgressRequest(6, 65, "Lead time reduced to six days."),
            "correlation-5",
            CancellationToken.None);
        goal = await fixture.Service.AddStatusUpdateAsync(
            goal.Id,
            new AddGoalStatusUpdateRequest(
                GoalStatuses.Active,
                GoalHealth.OnTrack,
                72,
                "Quarterly delivery is on track."),
            "correlation-6",
            CancellationToken.None);

        Assert.Equal(45, goal.Progress);
        Assert.Equal(40, goal.KeyResults.Single(item => item.Id == adoption.Id).Progress);
        Assert.Equal(50, goal.KeyResults.Single(item => item.Id == leadTime.Id).Progress);
        Assert.Equal("Quarterly delivery is on track.", Assert.Single(goal.StatusUpdates).Note);
        Assert.Equal(
            10,
            Assert.Single(goal.KeyResults.Single(item => item.Id == adoption.Id).ProgressUpdates)
                .PreviousValue);
        Assert.Equal(new DateOnly(2026, 7, 1), goal.PeriodStart);
        Assert.Equal(new DateOnly(2026, 9, 30), goal.PeriodEnd);
    }

    [Fact]
    public async Task HistoriesRetainTheMostRecentFiftyUpdates()
    {
        var fixture = new Fixture();
        var goal = await fixture.Service.SaveAsync(
            null,
            Request(),
            "correlation",
            CancellationToken.None);
        goal = await fixture.Service.SaveKeyResultAsync(
            goal.Id,
            null,
            KeyResult("Adoption", "owner-1", 0, 100, 0, KeyResultDirections.Increase),
            "correlation",
            CancellationToken.None);
        var keyResultId = Assert.Single(goal.KeyResults).Id;

        for (var index = 1; index <= 51; index++)
        {
            goal = await fixture.Service.AddKeyResultProgressAsync(
                goal.Id,
                keyResultId,
                new AddKeyResultProgressRequest(index, 50, $"Progress {index}"),
                "correlation",
                CancellationToken.None);
            goal = await fixture.Service.AddStatusUpdateAsync(
                goal.Id,
                new AddGoalStatusUpdateRequest(
                    GoalStatuses.Active,
                    GoalHealth.OnTrack,
                    50,
                    $"Status {index}"),
                "correlation",
                CancellationToken.None);
        }

        Assert.Equal(50, goal.StatusUpdates.Count);
        Assert.Equal("Status 51", goal.StatusUpdates.First().Note);
        Assert.Equal("Status 2", goal.StatusUpdates.Last().Note);
        Assert.Equal(ProjectHistoryRetentionPolicy.MaximumGoalStatusUpdates, goal.StatusUpdateRetentionLimit);
        var progress = Assert.Single(goal.KeyResults).ProgressUpdates;
        Assert.Equal(50, progress.Count);
        Assert.Equal("Progress 51", progress.First().Note);
        Assert.Equal("Progress 2", progress.Last().Note);
        Assert.Equal(
            ProjectHistoryRetentionPolicy.MaximumKeyResultProgressUpdates,
            Assert.Single(goal.KeyResults).ProgressUpdateRetentionLimit);
    }

    [Fact]
    public async Task KeyResultOwnerCanPublishProgressButCannotEditGoal()
    {
        var fixture = new Fixture();
        var goal = await fixture.Service.SaveAsync(
            null,
            Request(),
            "correlation",
            CancellationToken.None);
        goal = await fixture.Service.SaveKeyResultAsync(
            goal.Id,
            null,
            KeyResult("Activation", "viewer-1", 0, 100, 0, KeyResultDirections.Increase),
            "correlation",
            CancellationToken.None);
        var keyResult = Assert.Single(goal.KeyResults);

        fixture.Current.UserId = "viewer-1";
        var visible = await fixture.Service.GetAsync(goal.Id, false, CancellationToken.None);
        Assert.False(visible.CanEdit);
        Assert.True(Assert.Single(visible.KeyResults).CanUpdate);
        var updated = await fixture.Service.AddKeyResultProgressAsync(
            goal.Id,
            keyResult.Id,
            new AddKeyResultProgressRequest(25, 55, "Activation moved."),
            "correlation",
            CancellationToken.None);
        Assert.Equal(25, Assert.Single(updated.KeyResults).Progress);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            fixture.Service.SaveAsync(
                goal.Id,
                Request() with { Name = "Forbidden edit" },
                "correlation",
                CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            fixture.Service.AddStatusUpdateAsync(
                goal.Id,
                new AddGoalStatusUpdateRequest(
                    GoalStatuses.Active,
                    GoalHealth.OnTrack,
                    50,
                    "Forbidden status."),
                "correlation",
                CancellationToken.None));
    }

    [Fact]
    public async Task RejectsInvalidPeriodsDirectionsAndUnreadableLinks()
    {
        var fixture = new Fixture();
        await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.SaveAsync(
                null,
                Request() with
                {
                    PeriodStart = new DateOnly(2026, 10, 1),
                    PeriodEnd = new DateOnly(2026, 9, 30)
                },
                "correlation",
                CancellationToken.None));

        fixture.Directory.DeniedSources.Add("project:project-1");
        await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.SaveAsync(
                null,
                Request(),
                "correlation",
                CancellationToken.None));
        fixture.Directory.DeniedSources.Clear();

        var goal = await fixture.Service.SaveAsync(
            null,
            Request(),
            "correlation",
            CancellationToken.None);
        await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.SaveKeyResultAsync(
                goal.Id,
                null,
                KeyResult("Invalid", "owner-1", 10, 5, 10, KeyResultDirections.Increase),
                "correlation",
                CancellationToken.None));
    }

    [Fact]
    public async Task RollupIsPartialWhenLinkedSourcePermissionIsLost()
    {
        var fixture = new Fixture();
        var goal = await fixture.Service.SaveAsync(
            null,
            Request(),
            "correlation",
            CancellationToken.None);
        goal = await fixture.Service.SaveKeyResultAsync(
            goal.Id,
            null,
            KeyResult("Adoption", "owner-1", 0, 100, 60, KeyResultDirections.Increase),
            "correlation",
            CancellationToken.None);
        fixture.Directory.DeniedSources.Add("project:project-1");

        var rollup = await fixture.Service.GetRollupAsync(
            goal.Id,
            CancellationToken.None);

        Assert.Equal(GoalSourceStatuses.Partial, rollup.SourceStatus);
        Assert.Equal(60, rollup.Progress);
        Assert.Equal(["project:project-1"], rollup.UnavailableSources);
        Assert.Single(rollup.Initiatives);
        Assert.Empty(rollup.Projects);
    }

    private static SaveGoalRequest Request() =>
        new(
            "Increase delivery adoption",
            "Synthetic quarterly objective",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 9, 30),
            ["viewer-1"],
            [new GoalInitiativeLinkRequest("portfolio-1", "initiative-1")],
            ["project-1"]);

    private static SaveKeyResultRequest KeyResult(
        string name,
        string owner,
        decimal baseline,
        decimal target,
        decimal initial,
        string direction) =>
        new(name, null, owner, baseline, target, initial, "%", direction);

    private sealed class Fixture
    {
        public InMemoryDocumentRepository<GoalDocument> Repository { get; } = new();
        public GoalDirectory Directory { get; } = new();
        public CurrentUser Current { get; } = new();
        public GoalService Service { get; }

        public Fixture()
        {
            Service = new GoalService(
                Repository,
                Directory,
                new CapturingAudit(),
                Current,
                new FixedClock());
        }
    }

    private sealed class GoalDirectory : IGoalDirectory
    {
        public HashSet<string> DeniedSources { get; } = new(StringComparer.Ordinal);

        public Task EnsureOrganizationUsersAsync(
            string organizationId,
            IReadOnlyCollection<string> userIds,
            CancellationToken ct) => Task.CompletedTask;

        public async Task EnsureSourcesReadableAsync(
            string organizationId,
            IReadOnlyCollection<GoalInitiativeLinkRequest> initiativeLinks,
            IReadOnlyCollection<string> projectIds,
            CancellationToken ct)
        {
            var result = await ReadSourcesAsync(
                organizationId,
                initiativeLinks,
                projectIds,
                ct);
            if (result.UnavailableSources.Count > 0)
                throw new ValidationException("Goal source is not readable.");
        }

        public Task<GoalSourceResult> ReadSourcesAsync(
            string organizationId,
            IReadOnlyCollection<GoalInitiativeLinkRequest> initiativeLinks,
            IReadOnlyCollection<string> projectIds,
            CancellationToken ct)
        {
            var initiatives = initiativeLinks
                .Where(item => !DeniedSources.Contains(
                    $"initiative:{item.PortfolioId}:{item.InitiativeId}"))
                .Select(item => new GoalInitiativeSource(
                    item.PortfolioId,
                    item.InitiativeId,
                    "Initiative",
                    InitiativeStatuses.Active,
                    InitiativeHealth.OnTrack,
                    80))
                .ToList();
            var projects = projectIds
                .Where(item => !DeniedSources.Contains($"project:{item}"))
                .Select(item => new GoalProjectSource(item, "PRJ", "Project"))
                .ToList();
            var unavailable = initiativeLinks
                .Select(item => $"initiative:{item.PortfolioId}:{item.InitiativeId}")
                .Concat(projectIds.Select(item => $"project:{item}"))
                .Where(DeniedSources.Contains)
                .ToList();
            return Task.FromResult(new GoalSourceResult(
                initiatives,
                projects,
                unavailable));
        }
    }

    private sealed class CapturingAudit : IGoalAuditWriter
    {
        public Task WriteAsync(
            string action,
            string goalId,
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
        public DateTimeOffset UtcNow => new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);
    }
}
