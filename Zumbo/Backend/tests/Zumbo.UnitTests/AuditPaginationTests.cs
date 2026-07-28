using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Audit;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class AuditPaginationTests
{
    [Fact]
    public async Task ExactPageDoesNotAdvertiseANonexistentNextPage()
    {
        var repository = new InMemoryDocumentRepository<AuditLogDocument>();
        await CreateAsync(repository, "a");
        await CreateAsync(repository, "b");
        var service = CreateService(repository);

        var page = await service.QueryAsync(
            new AuditLogQuery(null, "WorkItemMoved", null, null, null, null, Page: 1, PageSize: 2),
            CancellationToken.None);

        Assert.Equal(2, page.Items.Count);
        Assert.False(page.HasNextPage);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task CursorContinuesAcrossRecordsWithTheSameTimestamp()
    {
        var repository = new InMemoryDocumentRepository<AuditLogDocument>();
        await CreateAsync(repository, "a");
        await CreateAsync(repository, "b");
        await CreateAsync(repository, "c");
        var service = CreateService(repository);

        var first = await service.QueryAsync(
            new AuditLogQuery(null, "WorkItemMoved", null, null, null, null, Page: 1, PageSize: 2),
            CancellationToken.None);
        var second = await service.QueryAsync(
            new AuditLogQuery(null, "WorkItemMoved", null, null, null, null, PageSize: 2, Cursor: first.NextCursor),
            CancellationToken.None);

        Assert.Equal(["a", "b"], first.Items.Select(item => item.Id));
        Assert.True(first.HasNextPage);
        Assert.Equal("c", Assert.Single(second.Items).Id);
        Assert.False(second.HasNextPage);
    }

    private static AuditService CreateService(InMemoryDocumentRepository<AuditLogDocument> repository) =>
        new(
            repository,
            new FixedClock(),
            new FixedCurrentUser(),
            new EmptyRequestContext(),
            new AllowAccessChecker());

    private static Task CreateAsync(
        InMemoryDocumentRepository<AuditLogDocument> repository,
        string id) =>
        repository.CreateAsync(new AuditLogDocument
        {
            Id = id,
            OrganizationId = "org-1",
            ActorUserId = "user-1",
            Action = "WorkItemMoved",
            EntityType = "WorkItem",
            EntityId = "item-1",
            CreatedAt = FixedClock.Timestamp
        });

    private sealed class FixedClock : IClock
    {
        internal static readonly DateTimeOffset Timestamp =
            new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => Timestamp;
    }

    private sealed class FixedCurrentUser : ICurrentUser
    {
        public string? UserId => "user-1";
        public string? OrganizationId => "org-1";
        public IReadOnlyCollection<string> Roles => ["Owner"];
    }

    private sealed class EmptyRequestContext : IAuditRequestContext
    {
        public AuditRequestMetadata GetMetadata() => new(null, null);
    }

    private sealed class AllowAccessChecker : IAuditAccessChecker
    {
        public Task<AuditReadScope> EnsureCanReadAsync(AuditLogQuery query, CancellationToken ct) =>
            Task.FromResult(new AuditReadScope("org-1"));
    }
}
