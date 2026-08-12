using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed class IdentityRoleCatalogService(
    IDocumentRepository<IdentityRoleDocument> roles,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock)
{
    private const int SeedVersion = 1;

    public async Task EnsureSeededAsync(CancellationToken ct)
    {
        var current = await roles.ListByFilterAsync(
            x => true,
            pageSize: 200,
            cancellationToken: ct);
        if (PermissionCatalog.SystemRoles.Keys.All(name => current.Any(role =>
            role.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            && role.Scope == "System"
            && role.SeedVersion >= SeedVersion))
            && PermissionCatalog.ProjectRoles.Keys.All(name => current.Any(role =>
                role.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                && role.Scope == "Project"
                && role.SeedVersion >= SeedVersion))
            && current.All(role => role.Version > 0))
        {
            return;
        }

        var options = distributedLockOptions.Value;
        await using var roleLock = await distributedLockProvider.TryAcquireAsync(
            "identity-system-role-seed-v" + SeedVersion,
            TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
            TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
            ct) ?? throw new ConflictException("IDENTITY_CATALOG_BUSY", "Role catalog is being initialized; retry the operation.");

        foreach (var legacyRole in current.Where(role => role.Version <= 0))
        {
            legacyRole.Version = 1;
            await roles.ReplaceByFilterAsync(x => x.Id == legacyRole.Id, legacyRole, ct);
        }

        var order = 0;
        foreach (var definition in PermissionCatalog.SystemRoles)
        {
            order += 10;
            var existing = await roles.SelectAsync(x => x.IsSystem && x.Name == definition.Key, ct);
            if (existing is null)
            {
                var now = clock.UtcNow;
                await roles.CreateAsync(new IdentityRoleDocument
                {
                    Id = "system-role-" + definition.Key.ToLowerInvariant(),
                    Name = definition.Key,
                    DisplayName = DisplayName(definition.Key),
                    Description = Description(definition.Key),
                    Scope = "System",
                    IsSystem = true,
                    IsProtected = true,
                    IsDefault = definition.Key == "User",
                    DisplayOrder = order,
                    SeedVersion = SeedVersion,
                    Permissions = definition.Value.ToList(),
                    CreatedAt = now,
                    UpdatedAt = now
                }, ct);
                continue;
            }

            if (existing.SeedVersion >= SeedVersion)
            {
                continue;
            }

            existing.DisplayName = DisplayName(definition.Key);
            existing.Description = Description(definition.Key);
            existing.Scope = "System";
            existing.IsActive = true;
            existing.IsProtected = true;
            existing.IsDefault = definition.Key == "User";
            existing.DisplayOrder = order;
            existing.SeedVersion = SeedVersion;
            existing.Permissions = definition.Value.ToList();
            existing.UpdatedAt = clock.UtcNow;
            var result = await roles.ReplaceByVersionAsync(x => x.Id == existing.Id, existing, existing.Version, ct);
            if (!result.Found)
            {
                throw new ConflictException("ROLE_SEED_CONFLICT", "Role catalog changed while it was being initialized.");
            }
        }

        order = 0;
        foreach (var definition in PermissionCatalog.ProjectRoles)
        {
            order += 10;
            var existing = await roles.SelectAsync(
                x => x.IsSystem && x.Scope == "Project" && x.Name == definition.Key,
                ct);
            if (existing is null)
            {
                var now = clock.UtcNow;
                await roles.CreateAsync(new IdentityRoleDocument
                {
                    Id = "project-role-" + definition.Key.ToLowerInvariant(),
                    Name = definition.Key,
                    DisplayName = ProjectDisplayName(definition.Key),
                    Description = ProjectDescription(definition.Key),
                    Scope = "Project",
                    IsSystem = true,
                    IsProtected = definition.Key == "ProjectOwner",
                    IsDefault = definition.Key == "Developer",
                    DisplayOrder = order,
                    SeedVersion = SeedVersion,
                    Permissions = definition.Value.ToList(),
                    CreatedAt = now,
                    UpdatedAt = now
                }, ct);
                continue;
            }

            if (existing.SeedVersion >= SeedVersion)
            {
                continue;
            }

            existing.DisplayName = ProjectDisplayName(definition.Key);
            existing.Description = ProjectDescription(definition.Key);
            existing.Scope = "Project";
            existing.IsActive = true;
            existing.IsProtected = definition.Key == "ProjectOwner";
            existing.IsDefault = definition.Key == "Developer";
            existing.DisplayOrder = order;
            existing.SeedVersion = SeedVersion;
            existing.Permissions = definition.Value.ToList();
            existing.UpdatedAt = clock.UtcNow;
            var result = await roles.ReplaceByVersionAsync(x => x.Id == existing.Id, existing, existing.Version, ct);
            if (!result.Found)
            {
                throw new ConflictException("ROLE_SEED_CONFLICT", "Project role catalog changed while it was being initialized.");
            }
        }
    }

    public async Task<bool> HasPermissionAsync(
        IReadOnlyCollection<string> assignedRoleNames,
        string organizationId,
        string permission,
        CancellationToken ct)
    {
        await EnsureSeededAsync(ct);
        var definitions = await roles.ListByFilterAsync(
            x => x.IsActive && x.Scope != "Project" && (x.IsSystem || x.OrganizationId == organizationId),
            pageSize: 200,
            cancellationToken: ct);
        return definitions.Any(role =>
            assignedRoleNames.Contains(role.Name, StringComparer.OrdinalIgnoreCase)
            && role.Permissions.Any(value => value == PermissionCatalog.All
                || value.Equals(permission, StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<bool> HasProjectPermissionAsync(
        string roleName,
        string permission,
        CancellationToken ct)
    {
        await EnsureSeededAsync(ct);
        var role = await roles.SelectAsync(
            x => x.IsActive && x.Scope == "Project" && x.Name == roleName,
            ct);
        return role is not null && role.Permissions.Any(value =>
            value == PermissionCatalog.All
            || value.Equals(permission, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> CanGrantPermissionsAsync(
        IReadOnlyCollection<string> assignedRoleNames,
        string organizationId,
        IReadOnlyCollection<string> requestedPermissions,
        CancellationToken ct)
    {
        await EnsureSeededAsync(ct);
        var definitions = await roles.ListByFilterAsync(
            x => x.IsActive && x.Scope != "Project" && (x.IsSystem || x.OrganizationId == organizationId),
            pageSize: 200,
            cancellationToken: ct);
        var grants = definitions
            .Where(role => assignedRoleNames.Contains(role.Name, StringComparer.OrdinalIgnoreCase))
            .SelectMany(role => role.Permissions)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return grants.Contains(PermissionCatalog.All)
            || requestedPermissions.All(grants.Contains);
    }

    private static string DisplayName(string role) => role switch
    {
        "User" => "Kullanıcı",
        "OrganizationAdmin" => "Organizasyon yöneticisi",
        "AuditReader" => "Denetim okuyucusu",
        "SystemAdmin" => "Sistem yöneticisi",
        _ => role
    };

    private static string Description(string role) => role switch
    {
        "User" => "Temel hesap ve profil erişimi.",
        "OrganizationAdmin" => "Organizasyon, rol ve entegrasyon yönetimi.",
        "AuditReader" => "Tüm yetkili denetim kayıtlarına salt okunur erişim.",
        "SystemAdmin" => "Korunan tüm sistem yetkileri.",
        _ => role
    };

    private static string ProjectDisplayName(string role) => role switch
    {
        "ProjectOwner" => "Proje sahibi",
        "ProjectAdmin" => "Proje yöneticisi",
        "Developer" => "Geliştirici",
        "Viewer" => "Görüntüleyici",
        _ => role
    };

    private static string ProjectDescription(string role) => role switch
    {
        "ProjectOwner" => "Projenin korunan sahibi ve tam yöneticisi.",
        "ProjectAdmin" => "Proje yapılandırmasını ve üyelerini yönetir.",
        "Developer" => "Proje işlerini oluşturur, günceller ve taşır.",
        "Viewer" => "Projeyi görüntüler ve yorum yapar.",
        _ => role
    };
}
