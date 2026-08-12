using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class IdentityPermissionCatalogTests
{
    [Fact]
    public async Task RuntimeRoleDefinition_GrantsAndRevokesWithoutRestart()
    {
        var repository = new InMemoryDocumentRepository<IdentityRoleDocument>();
        var catalog = new IdentityRoleCatalogService(
            repository,
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            new FixedClock());
        var role = await repository.CreateAsync(new IdentityRoleDocument
        {
            Name = "Release Manager",
            DisplayName = "Sürüm yöneticisi",
            OrganizationId = "org-1",
            Permissions = ["Release.Publish"],
            CreatedAt = new FixedClock().UtcNow,
            UpdatedAt = new FixedClock().UtcNow
        }, CancellationToken.None);

        Assert.True(await catalog.HasPermissionAsync(
            [role.Name], "org-1", "Release.Publish", CancellationToken.None));

        role.IsActive = false;
        var replaced = await repository.ReplaceByVersionAsync(
            x => x.Id == role.Id,
            role,
            role.Version,
            CancellationToken.None);
        Assert.True(replaced.Found);

        Assert.False(await catalog.HasPermissionAsync(
            [role.Name], "org-1", "Release.Publish", CancellationToken.None));
    }

    [Fact]
    public async Task SeededProjectRoles_AreEvaluatedFromPersistedDefinitions()
    {
        var repository = new InMemoryDocumentRepository<IdentityRoleDocument>();
        var catalog = new IdentityRoleCatalogService(
            repository,
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            new FixedClock());

        Assert.True(await catalog.HasProjectPermissionAsync(
            "ProjectAdmin", PermissionCatalog.BoardManage, CancellationToken.None));
        Assert.False(await catalog.HasProjectPermissionAsync(
            "Viewer", PermissionCatalog.BoardManage, CancellationToken.None));

        var projectRoles = await repository.ListByFilterAsync(
            role => role.Scope == "Project",
            cancellationToken: CancellationToken.None);
        Assert.Contains(projectRoles, role => role.IsDefault && role.Name == "Developer");
        Assert.Contains(projectRoles, role => role.IsProtected && role.Name == "ProjectOwner");
    }

    [Fact]
    public async Task RoleManager_CannotGrantPermissionsOutsideOwnEffectiveGrants()
    {
        var catalog = new IdentityRoleCatalogService(
            new InMemoryDocumentRepository<IdentityRoleDocument>(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            new FixedClock());

        Assert.True(await catalog.CanGrantPermissionsAsync(
            ["OrganizationAdmin"],
            "org-1",
            [PermissionCatalog.OrganizationManage],
            CancellationToken.None));
        Assert.False(await catalog.CanGrantPermissionsAsync(
            ["OrganizationAdmin"],
            "org-1",
            [PermissionCatalog.ReleasePublish],
            CancellationToken.None));
        Assert.True(await catalog.CanGrantPermissionsAsync(
            ["SystemAdmin"],
            "org-1",
            [PermissionCatalog.ReleasePublish],
            CancellationToken.None));
    }

    [Fact]
    public async Task Seed_IsIdempotent_AndCustomizedMetadataRemainsRuntimeData()
    {
        var repository = new InMemoryDocumentRepository<IdentityPermissionDefinitionDocument>();
        var audit = new RecordingAuditWriter();
        var service = new IdentityPermissionCatalogService(
            repository,
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            audit,
            new FixedClock());

        var first = await service.ListAsync(CancellationToken.None);
        Assert.Equal(
            PermissionCatalog.EndpointPermissions.Order(StringComparer.OrdinalIgnoreCase),
            first.Select(item => item.Key).Order(StringComparer.OrdinalIgnoreCase));
        var workItemView = Assert.Single(first, item => item.Key == "WorkItemView");
        var updated = await service.UpdateAsync(
            workItemView.Key,
            new UpdatePermissionDefinitionRequest(
                "Proje işlerini incele",
                "Yetkili proje kapsamındaki iş kayıtlarını görüntüler.",
                "Proje işleri",
                205,
                true,
                workItemView.Version),
            "catalog-test",
            CancellationToken.None);
        var second = await service.ListAsync(CancellationToken.None);

        Assert.Equal(first.Count, second.Count);
        Assert.Equal("Proje işlerini incele", Assert.Single(second, item => item.Key == "WorkItemView").Label);
        Assert.Equal(updated.Version, Assert.Single(second, item => item.Key == "WorkItemView").Version);
        Assert.Contains("PermissionMetadataUpdated", audit.Actions);
    }

    [Fact]
    public async Task Update_RejectsStaleVersion()
    {
        var service = new IdentityPermissionCatalogService(
            new InMemoryDocumentRepository<IdentityPermissionDefinitionDocument>(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            new RecordingAuditWriter(),
            new FixedClock());
        var definition = Assert.Single(
            await service.ListAsync(CancellationToken.None),
            item => item.Key == "BoardView");

        var exception = await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(
            definition.Key,
            new UpdatePermissionDefinitionRequest(
                definition.Label,
                definition.Description,
                definition.Category,
                definition.DisplayOrder,
                definition.IsActive,
                definition.Version + 1),
            "catalog-test",
            CancellationToken.None));

        Assert.Equal("PERMISSION_DEFINITION_CONFLICT", exception.Code);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class RecordingAuditWriter : IIdentityAuditWriter
    {
        public List<string> Actions { get; } = [];

        public Task WriteAsync(
            string action,
            string entityId,
            string? oldValue,
            string? newValue,
            string correlationId,
            CancellationToken ct)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }
    }
}
