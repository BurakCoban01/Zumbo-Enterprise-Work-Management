using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class ProjectLifecycleTests
{
    [Fact]
    public async Task RestoreRejectsExpiredRetentionWindow()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 7, 20, 14, 0, 0, TimeSpan.Zero));
        var currentUser = new CurrentUserStub
        {
            UserId = "owner-1",
            OrganizationId = "org-1"
        };
        var service = new ProjectService(
            new InMemoryDocumentRepository<ProjectDocument>(),
            new AllowMembers(),
            new NoTeams(),
            new NoTeamUsage(),
            new NoAudit(),
            clock,
            currentUser,
            lifecycleOptions: Options.Create(new ProjectLifecycleOptions { ArchiveRetentionDays = 30 }));
        var project = await service.CreateAsync(
            new CreateProjectRequest("org-1", "RET", "Retention", "owner-1"),
            CancellationToken.None);
        await service.ArchiveAsync(project.Id, CancellationToken.None);

        clock.UtcNow = clock.UtcNow.AddDays(31);
        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            service.RestoreAsync(project.Id, "test", CancellationToken.None));

        Assert.Equal("PROJECT_RETENTION_EXPIRED", exception.Code);
    }

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    private sealed class CurrentUserStub : ICurrentUser
    {
        public string UserId { get; set; } = string.Empty;
        public string OrganizationId { get; set; } = string.Empty;
        public IReadOnlyCollection<string> Roles { get; set; } = ["User"];
    }

    private sealed class AllowMembers : IProjectMemberDirectory
    {
        public Task EnsureEligibleAsync(string userId, string organizationId, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class NoTeams : IProjectTeamDirectory
    {
        public Task<ProjectTeamDirectoryEntry?> FindAsync(string teamId, CancellationToken ct) =>
            Task.FromResult<ProjectTeamDirectoryEntry?>(null);
    }

    private sealed class NoTeamUsage : IProjectTeamUsageChecker
    {
        public Task<bool> HasWorkItemsAsync(string projectId, string teamId, CancellationToken ct) =>
            Task.FromResult(false);
    }

    private sealed class NoAudit : IProjectAuditWriter
    {
        public Task WriteAsync(
            string action,
            string entityId,
            string? oldValue,
            string? newValue,
            string correlationId,
            CancellationToken ct) => Task.CompletedTask;
    }
}
