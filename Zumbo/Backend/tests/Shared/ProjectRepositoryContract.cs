using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Projects;

namespace Zumbo.RepositoryContracts;

public abstract class ProjectRepositoryContract
{
    protected abstract Task<ProjectRepositoryFixture> CreateFixtureAsync();

    [Fact]
    public async Task OwnershipCatalogRetentionAndConcurrency_AreProviderNeutral()
    {
        await using var fixture = await CreateFixtureAsync();
        var stamp = Guid.NewGuid().ToString("N");
        var id = "project-contract-" + stamp;
        var created = await fixture.Repository.CreateAsync(new ProjectDocument
        {
            Id = id,
            OrganizationId = "organization-" + stamp,
            Key = "PC" + stamp[..6].ToUpperInvariant(),
            Name = "Project Contract",
            Visibility = ProjectVisibilities.Private,
            Members =
            [
                new ProjectMemberDocument { UserId = "owner-1", Role = ProjectRoles.Owner },
                new ProjectMemberDocument { UserId = "owner-2", Role = ProjectRoles.Admin }
            ],
            TeamIds = ["team-1"],
            Templates =
            [
                new ProjectTemplateDocument
                {
                    Name = "Default Template",
                    IsDefault = true,
                    DefaultComponentNames = ["API"]
                }
            ],
            Components = [new ProjectComponentDocument { Name = "API" }],
            Versions = [new ProjectVersionDocument { Name = "1.0" }],
            Milestones =
            [
                new ProjectMilestoneDocument
                {
                    Name = "Launch",
                    DueAt = fixture.Now.AddDays(30)
                }
            ],
            CreatedAt = fixture.Now,
            UpdatedAt = fixture.Now
        });
        Assert.Equal(1, created.Version);

        var firstWriter = await fixture.Repository.SelectAsync(project => project.Id == id);
        var staleWriter = await fixture.Repository.SelectAsync(project => project.Id == id);
        Assert.NotNull(firstWriter);
        Assert.NotNull(staleWriter);
        firstWriter!.Members.Single(member => member.UserId == "owner-1").Role = ProjectRoles.Admin;
        firstWriter.Members.Single(member => member.UserId == "owner-2").Role = ProjectRoles.Owner;
        firstWriter.Archived = true;
        firstWriter.ArchivedAt = fixture.Now;
        firstWriter.RetainUntil = fixture.Now.AddDays(90);
        var changed = await fixture.Repository.ReplaceByVersionAsync(
            project => project.Id == id,
            firstWriter,
            firstWriter.Version);
        Assert.Equal(2, changed.Version);

        staleWriter!.Name = "Stale writer";
        var conflict = await Assert.ThrowsAsync<DocumentConcurrencyException>(() =>
            fixture.Repository.ReplaceByVersionAsync(
                project => project.Id == id,
                staleWriter,
                staleWriter.Version));
        Assert.Equal(1, conflict.ExpectedVersion);
        Assert.Equal(2, conflict.ActualVersion);

        var persisted = await fixture.Repository.SelectAsync(project => project.Id == id);
        Assert.NotNull(persisted);
        Assert.Single(persisted!.Members, member => member.Role == ProjectRoles.Owner);
        Assert.Equal("owner-2", persisted.Members.Single(member => member.Role == ProjectRoles.Owner).UserId);
        Assert.True(persisted.Archived);
        Assert.Equal(fixture.Now.AddDays(90), persisted.RetainUntil);
        Assert.Single(persisted.Templates);
        Assert.Single(persisted.Components);
        Assert.Single(persisted.Versions);
        Assert.Single(persisted.Milestones);
        Assert.Equal(2, persisted.Version);
    }
}

public abstract class ProjectRepositoryFixture(
    IDocumentRepository<ProjectDocument> repository) : IAsyncDisposable
{
    public IDocumentRepository<ProjectDocument> Repository { get; } = repository;
    public DateTimeOffset Now { get; } = new(2026, 7, 20, 14, 0, 0, TimeSpan.Zero);
    public abstract ValueTask DisposeAsync();
}
