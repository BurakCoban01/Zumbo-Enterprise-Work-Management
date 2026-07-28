using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class DashboardServiceTests
{
    [Fact]
    public async Task OwnerCanCreateShareAndViewerCannotEdit()
    {
        var fixture = new Fixture();
        var created = await fixture.Service.SaveAsync(
            null,
            Request(DashboardScopes.Personal, ["project-1"]),
            "correlation-1",
            CancellationToken.None);

        Assert.True(created.CanEdit);
        Assert.Equal(1, created.Version);
        Assert.Equal("DashboardCreated", Assert.Single(fixture.Audit.Actions));

        var shared = await fixture.Service.ShareAsync(
            created.Id,
            new ShareDashboardRequest(["viewer-1"]),
            "correlation-2",
            CancellationToken.None);
        Assert.Equal(["viewer-1"], shared.ViewerUserIds);

        fixture.User.UserIdValue = "viewer-1";
        var page = await fixture.Service.ListAsync(false, 1, 20, CancellationToken.None);
        var visible = Assert.Single(page.Items);
        Assert.False(visible.CanEdit);
        await Assert.ThrowsAsync<ForbiddenException>(() => fixture.Service.SaveAsync(
            created.Id,
            Request(DashboardScopes.Personal, ["project-1"]),
            "correlation-3",
            CancellationToken.None));
    }

    [Fact]
    public async Task SourcePermissionLossHidesSharedDashboardFromList()
    {
        var fixture = new Fixture();
        var created = await fixture.Service.SaveAsync(
            null,
            Request(DashboardScopes.Personal, ["project-1"]),
            "correlation-1",
            CancellationToken.None);
        await fixture.Service.ShareAsync(
            created.Id,
            new ShareDashboardRequest(["viewer-1"]),
            "correlation-2",
            CancellationToken.None);

        fixture.User.UserIdValue = "viewer-1";
        fixture.Permissions.DeniedUsers.Add("viewer-1");
        var page = await fixture.Service.ListAsync(false, 1, 20, CancellationToken.None);
        Assert.Empty(page.Items);
        await Assert.ThrowsAsync<ForbiddenException>(
            () => fixture.Service.GetAsync(created.Id, false, CancellationToken.None));
    }

    [Fact]
    public async Task DefinitionRejectsOverlapScopeAndQueryFanout()
    {
        var fixture = new Fixture();
        var overlapping = Request(
            DashboardScopes.Project,
            ["project-1"],
            [
                Widget("summary", DashboardWidgetTypes.ProjectSummary, 1, 1, 6),
                Widget("status", DashboardWidgetTypes.StatusDistribution, 6, 1, 6)
            ]);
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.SaveAsync(
            null, overlapping, "correlation", CancellationToken.None));

        await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.SaveAsync(
            null,
            Request(DashboardScopes.Portfolio, ["project-1"]),
            "correlation",
            CancellationToken.None));

        var projects = Enumerable.Range(1, 20).Select(index => $"project-{index}").ToList();
        var widgets = Enumerable.Range(1, 4)
            .Select(index => Widget(
                $"widget-{index}",
                DashboardWidgetTypes.ProjectSummary,
                ((index - 1) % 3) * 4 + 1,
                index,
                4))
            .ToList();
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.SaveAsync(
            null,
            Request(DashboardScopes.Portfolio, projects, widgets),
            "correlation",
            CancellationToken.None));
    }

    [Fact]
    public async Task MissingCollectionsReturnValidationErrors()
    {
        var fixture = new Fixture();
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.SaveAsync(
            null,
            Request(DashboardScopes.Personal, null!),
            "correlation",
            CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.SaveAsync(
            null,
            Request(DashboardScopes.Personal, ["project-1"]) with { Widgets = null! },
            "correlation",
            CancellationToken.None));

        var created = await fixture.Service.SaveAsync(
            null,
            Request(DashboardScopes.Personal, ["project-1"]),
            "correlation",
            CancellationToken.None);
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.ShareAsync(
            created.Id,
            new ShareDashboardRequest(null!),
            "correlation",
            CancellationToken.None));
    }

    private static SaveDashboardRequest Request(
        string scope,
        IReadOnlyCollection<string> projectIds,
        IReadOnlyCollection<DashboardWidgetRequest>? widgets = null) =>
        new(
            "Delivery overview",
            "Synthetic dashboard",
            scope,
            projectIds,
            widgets ?? [Widget("summary", DashboardWidgetTypes.ProjectSummary, 1, 1, 12)],
            new DashboardFilterRequest());

    private static DashboardWidgetRequest Widget(
        string id,
        string type,
        int column,
        int row,
        int width) =>
        new(id, type, id, column, row, width, 1);

    private sealed class Fixture
    {
        public MutableCurrentUser User { get; } = new();
        public AllowPermissionChecker Permissions { get; } = new();
        public CapturingAudit Audit { get; } = new();
        public DashboardService Service { get; }

        public Fixture()
        {
            Service = new DashboardService(
                new InMemoryDocumentRepository<DashboardDocument>(),
                Permissions,
                new AllowViewerDirectory(),
                Audit,
                User,
                new FixedClock());
        }
    }

    private sealed class AllowPermissionChecker : IProjectPermissionChecker
    {
        public HashSet<string> DeniedUsers { get; } = new(StringComparer.Ordinal);

        public Task<ProjectResourceAuthorization> EnsureCanAsync(
            string userId,
            string projectId,
            string permission,
            CancellationToken ct)
        {
            if (DeniedUsers.Contains(userId))
                throw new ForbiddenException("Project access denied.");
            return Task.FromResult(new ProjectResourceAuthorization(
                projectId,
                "organization-1",
                userId,
                "ProjectOwner",
                false));
        }
    }

    private sealed class AllowViewerDirectory : IDashboardViewerDirectory
    {
        public Task EnsureOrganizationUsersAsync(
            string organizationId,
            IReadOnlyCollection<string> userIds,
            CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class CapturingAudit : IDashboardAuditWriter
    {
        public List<string> Actions { get; } = [];

        public Task WriteAsync(
            string action,
            string dashboardId,
            string? oldValue,
            string? newValue,
            string correlationId,
            CancellationToken ct)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }
    }

    private sealed class MutableCurrentUser : ICurrentUser
    {
        public string UserIdValue { get; set; } = "owner-1";
        public string? UserId => UserIdValue;
        public string? OrganizationId => "organization-1";
        public IReadOnlyCollection<string> Roles => ["User"];
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);
    }
}
