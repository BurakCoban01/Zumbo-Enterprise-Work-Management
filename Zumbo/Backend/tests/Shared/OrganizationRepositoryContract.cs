using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Organizations;

namespace Zumbo.RepositoryContracts;

public abstract class OrganizationRepositoryContract
{
    protected abstract Task<OrganizationRepositoryFixture> CreateFixtureAsync();

    [Fact]
    public async Task LifecycleOwnershipRetentionAndConcurrency_AreProviderNeutral()
    {
        await using var fixture = await CreateFixtureAsync();
        var stamp = Guid.NewGuid().ToString("N");
        var id = "organization-contract-" + stamp;
        var created = await fixture.Repository.CreateAsync(new OrganizationDocument
        {
            Id = id,
            TenantKey = id,
            Name = "Organization Contract",
            OwnerUserId = "owner-1",
            Departments =
            [
                new DepartmentDocument
                {
                    Id = "department-" + stamp,
                    Name = "Engineering",
                    Members =
                    [
                        new DepartmentMemberDocument { UserId = "member-1", Position = "Engineer" },
                        new DepartmentMemberDocument { UserId = "member-2", Position = "Lead" }
                    ]
                }
            ],
            CreatedAt = fixture.Now,
            UpdatedAt = fixture.Now
        });
        Assert.Equal(1, created.Version);

        var firstWriter = await fixture.Repository.SelectAsync(document => document.Id == id);
        var staleWriter = await fixture.Repository.SelectAsync(document => document.Id == id);
        Assert.NotNull(firstWriter);
        Assert.NotNull(staleWriter);
        firstWriter!.OwnerUserId = "owner-2";
        firstWriter.Status = OrganizationStatuses.Archived;
        firstWriter.ArchivedAt = fixture.Now;
        firstWriter.RetainUntil = fixture.Now.AddDays(90);
        var changed = await fixture.Repository.ReplaceByVersionAsync(
            document => document.Id == id,
            firstWriter,
            firstWriter.Version);
        Assert.Equal(2, changed.Version);

        staleWriter!.Name = "Stale writer";
        var conflict = await Assert.ThrowsAsync<DocumentConcurrencyException>(() =>
            fixture.Repository.ReplaceByVersionAsync(
                document => document.Id == id,
                staleWriter,
                staleWriter.Version));
        Assert.Equal(1, conflict.ExpectedVersion);
        Assert.Equal(2, conflict.ActualVersion);

        var persisted = await fixture.Repository.SelectAsync(document => document.Id == id);
        Assert.NotNull(persisted);
        Assert.Equal("owner-2", persisted!.OwnerUserId);
        Assert.Equal(OrganizationStatuses.Archived, persisted.Status);
        Assert.Equal(fixture.Now.AddDays(90), persisted.RetainUntil);
        Assert.Equal(2, persisted.Departments.Single().Members.Count);
        Assert.Equal(2, persisted.Version);
    }
}

public abstract class OrganizationRepositoryFixture(
    IDocumentRepository<OrganizationDocument> repository) : IAsyncDisposable
{
    public IDocumentRepository<OrganizationDocument> Repository { get; } = repository;
    public DateTimeOffset Now { get; } = new(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);

    public abstract ValueTask DisposeAsync();
}
