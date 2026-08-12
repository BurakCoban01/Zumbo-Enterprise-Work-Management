using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed class IdentityAdministrationService(
    IDocumentRepository<UserDocument> users,
    IDocumentRepository<IdentityRoleDocument> roles,
    IRefreshSessionStore sessions,
    IDurableTransactionRunner transactions,
    IdentityPermissionService permissionService,
    IdentityRoleCatalogService roleCatalog,
    IdentityPermissionCatalogService permissionCatalog,
    IIdentityAuditWriter audit,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<RoleResponse>> ListRolesAsync(CancellationToken ct, string? scope = null)
    {
        var actor = await GetActorAsync(ct);
        await EnsureSystemRolesAsync(ct);
        var normalizedScope = scope?.Trim();
        var result = string.IsNullOrEmpty(normalizedScope)
            ? await roles.ListByFilterAsync(
                x => x.Scope != "Project" && (x.IsSystem || x.OrganizationId == actor.OrganizationId),
                x => x.Name,
                pageSize: 200,
                cancellationToken: ct)
            : await roles.ListByFilterAsync(
                x => x.Scope == normalizedScope && (x.IsSystem || x.OrganizationId == actor.OrganizationId),
                x => x.Name,
                pageSize: 200,
                cancellationToken: ct);
        return result.Select(ToResponse).ToList();
    }

    public async Task<RoleResponse> CreateRoleAsync(
        CreateRoleRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = await RequireRoleManagerAsync(ct);
        var organizationId = NormalizeOrganizationId(request.OrganizationId);
        await EnsureOrganizationScopeAsync(actor, organizationId, ct);
        var name = NormalizeRoleName(request.Name);
        EnsureNotReserved(name);
        var permissions = await NormalizePermissionsAsync(actor, request.Permissions, ct);
        await using var roleLock = await AcquireLockAsync("identity-roles:" + organizationId, ct);
        if (await roles.SelectAsync(x => x.OrganizationId == organizationId && x.Name.ToLower() == name.ToLower(), ct) is not null)
        {
            throw new ConflictException("ROLE_NAME_EXISTS", "Role name must be unique inside the organization.");
        }

        var now = clock.UtcNow;
        var role = new IdentityRoleDocument
        {
            Name = name,
            DisplayName = name,
            Description = "Özel organizasyon rolü.",
            Scope = "Organization",
            OrganizationId = organizationId,
            DisplayOrder = 1000,
            Permissions = permissions,
            CreatedAt = now,
            UpdatedAt = now
        };
        await roles.CreateAsync(role, ct);
        await audit.WriteAsync("RoleCreated", role.Id, null, role.Name, correlationId, ct);
        return ToResponse(role);
    }

    public async Task<RoleResponse> UpdateRoleAsync(
        string roleId,
        UpdateRoleRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = await RequireRoleManagerAsync(ct);
        await using var roleLock = await AcquireLockAsync("identity-role:" + roleId, ct);
        var role = await roles.SelectAsync(x => x.Id == roleId, ct)
            ?? throw new NotFoundException("ROLE_NOT_FOUND", "Role was not found.");
        await EnsureCustomRoleAccessAsync(actor, role, ct);
        var name = NormalizeRoleName(request.Name);
        EnsureNotReserved(name);
        if (await roles.SelectAsync(x =>
            x.Id != role.Id
            && x.OrganizationId == role.OrganizationId
            && x.Name.ToLower() == name.ToLower(), ct) is not null)
        {
            throw new ConflictException("ROLE_NAME_EXISTS", "Role name must be unique inside the organization.");
        }

        if (!role.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            && await users.SelectAsync(x => x.Roles.Contains(role.Name), ct) is not null)
        {
            throw new ConflictException(
                "ROLE_NAME_IN_USE",
                "An assigned role cannot be renamed. Remove its assignments first.");
        }

        var oldValue = role.Name + ":" + string.Join(',', role.Permissions);
        role.Name = name;
        role.Permissions = await NormalizePermissionsAsync(actor, request.Permissions, ct);
        role.IsActive = request.IsActive ?? role.IsActive;
        role.UpdatedAt = clock.UtcNow;
        if (request.Version is not null && request.Version != role.Version)
        {
            throw new ConflictException("ROLE_VERSION_CONFLICT", "Role changed concurrently; reload and retry.");
        }

        var result = await roles.ReplaceByVersionAsync(x => x.Id == role.Id, role, role.Version, ct);
        if (!result.Found)
        {
            throw new ConflictException("ROLE_VERSION_CONFLICT", "Role changed concurrently; reload and retry.");
        }

        role.Version = result.Version!.Value;
        await audit.WriteAsync("RoleUpdated", role.Id, oldValue, role.Name + ":" + string.Join(',', role.Permissions), correlationId, ct);
        return ToResponse(role);
    }

    public async Task DeleteRoleAsync(string roleId, string correlationId, CancellationToken ct)
    {
        var actor = await RequireRoleManagerAsync(ct);
        await using var roleLock = await AcquireLockAsync("identity-role:" + roleId, ct);
        var role = await roles.SelectAsync(x => x.Id == roleId, ct)
            ?? throw new NotFoundException("ROLE_NOT_FOUND", "Role was not found.");
        await EnsureCustomRoleAccessAsync(actor, role, ct);
        if (await users.SelectAsync(x => x.Roles.Contains(role.Name), ct) is not null)
        {
            throw new ConflictException("ROLE_IN_USE", "A role assigned to users cannot be deleted.");
        }

        await roles.DeleteByFilterAsync(x => x.Id == role.Id, ct);
        await audit.WriteAsync("RoleDeleted", role.Id, role.Name, null, correlationId, ct);
    }

    public async Task<UserProfileResponse> AssignRolesAsync(
        string userId,
        AssignUserRolesRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = await RequireRoleManagerAsync(ct);
        await using var userLock = await AcquireLockAsync("identity-user:" + userId, ct);
        var target = await users.SelectAsync(x => x.Id == userId, ct)
            ?? throw new NotFoundException("USER_NOT_FOUND", "User was not found.");
        await EnsureOrganizationScopeAsync(actor, target.OrganizationId, ct);
        var requestedRoles = (request.Roles ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        await EnsureSystemRolesAsync(ct);
        var available = await roles.ListByFilterAsync(
            x => x.IsActive && x.Scope != "Project" && (x.IsSystem || x.OrganizationId == target.OrganizationId),
            pageSize: 200,
            cancellationToken: ct);
        foreach (var defaultRole in available.Where(role => role.IsDefault))
        {
            if (!requestedRoles.Contains(defaultRole.Name, StringComparer.OrdinalIgnoreCase))
            {
                requestedRoles.Insert(0, defaultRole.Name);
            }
        }

        if (requestedRoles.Count > 20)
        {
            throw new ValidationException("A user cannot have more than 20 roles.");
        }

        if (requestedRoles.Any(requested => available.All(x => !x.Name.Equals(requested, StringComparison.OrdinalIgnoreCase))))
        {
            throw new ValidationException("Every assigned role must be a defined system or organization role.");
        }

        var actorIsSystemAdmin = available.Any(role =>
            actor.Roles.Contains(role.Name, StringComparer.OrdinalIgnoreCase)
            && role.Permissions.Contains(PermissionCatalog.All, StringComparer.OrdinalIgnoreCase));
        if (!actorIsSystemAdmin)
        {
            var protectedRoleNames = available
                .Where(role => role.IsProtected)
                .Select(role => role.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (target.Roles.Any(protectedRoleNames.Contains)
                || requestedRoles.Any(protectedRoleNames.Contains))
            {
                throw new ForbiddenException("Organization admins cannot manage privileged system roles.");
            }
        }

        if (PermissionCatalog.IsSystemAdministrator(target.Roles)
            && !PermissionCatalog.IsSystemAdministrator(requestedRoles))
        {
            var admins = await users.ListByFilterAsync(
                x => x.IsActive && x.Roles.Contains("SystemAdmin"),
                pageSize: 2,
                cancellationToken: ct);
            if (admins.Count <= 1)
            {
                throw new ConflictException("LAST_SYSTEM_ADMIN", "The last active system administrator cannot be removed.");
            }
        }

        var oldRoles = string.Join(',', target.Roles);
        target.Roles = requestedRoles;
        target.SecurityStamp = Guid.NewGuid().ToString("N");
        var now = clock.UtcNow;
        LegacyRefreshSessionCompatibility.RevokeAll(target, now);
        await transactions.ExecuteAsync(
            "Identity",
            async token =>
            {
                await sessions.RevokeAllAsync(target.Id, target.OrganizationId, now, token);
                var result = await users.ReplaceByVersionAsync(
                    x => x.Id == target.Id && x.OrganizationId == target.OrganizationId,
                    target,
                    target.Version,
                    token);
                if (!result.Found)
                {
                    throw new NotFoundException("USER_NOT_FOUND", "User was not found.");
                }

                target.Version = result.Version!.Value;
            },
            ct);
        await audit.WriteAsync("UserRolesChanged", target.Id, oldRoles, string.Join(',', target.Roles), correlationId, ct);
        return IdentityMappings.ToProfile(target);
    }

    private async Task EnsureSystemRolesAsync(CancellationToken ct)
    {
        await roleCatalog.EnsureSeededAsync(ct);
    }

    private async Task<UserDocument> RequireRoleManagerAsync(CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        if (!await permissionService.HasPermissionAsync(PermissionCatalog.UserRoleManage, ct))
        {
            throw new ForbiddenException("User role management permission is required.");
        }

        return actor;
    }

    private async Task<UserDocument> GetActorAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required.");
        return await users.SelectAsync(x => x.Id == userId && x.IsActive, ct)
            ?? throw new UnauthorizedException("Authenticated user was not found.");
    }

    private async Task EnsureOrganizationScopeAsync(
        UserDocument actor,
        string organizationId,
        CancellationToken ct)
    {
        if (!await roleCatalog.HasPermissionAsync(
                actor.Roles,
                actor.OrganizationId,
                PermissionCatalog.All,
                ct)
            && actor.OrganizationId != organizationId)
        {
            throw new ForbiddenException("Role management is limited to the current organization.");
        }
    }

    private async Task EnsureCustomRoleAccessAsync(
        UserDocument actor,
        IdentityRoleDocument role,
        CancellationToken ct)
    {
        if (role.IsSystem)
        {
            throw new ConflictException("SYSTEM_ROLE_LOCKED", "System roles cannot be changed or deleted.");
        }

        await EnsureOrganizationScopeAsync(actor, role.OrganizationId!, ct);
    }

    private static string NormalizeOrganizationId(string? organizationId)
    {
        if (string.IsNullOrWhiteSpace(organizationId))
        {
            throw new ValidationException("Organization id is required.");
        }

        return organizationId.Trim();
    }

    private static string NormalizeRoleName(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (!Regex.IsMatch(normalized, "^[A-Za-z][A-Za-z0-9 _-]{1,49}$"))
        {
            throw new ValidationException("Role name must contain 2-50 letters, numbers, spaces, hyphens or underscores.");
        }

        return normalized;
    }

    private static void EnsureNotReserved(string name)
    {
        if (PermissionCatalog.SystemRoles.ContainsKey(name))
        {
            throw new ConflictException("SYSTEM_ROLE_NAME_RESERVED", "System role names are reserved.");
        }
    }

    private async Task<List<string>> NormalizePermissionsAsync(
        UserDocument actor,
        IReadOnlyCollection<string>? permissions,
        CancellationToken ct)
    {
        var result = (permissions ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (result.Count is 0 or > 100 || result.Any(x => !Regex.IsMatch(x, "^[A-Za-z][A-Za-z0-9.:-]{1,79}$")))
        {
            throw new ValidationException("A role requires 1-100 valid permission names.");
        }

        var assignable = await permissionCatalog.ListAsync(ct);
        if (result.Any(x => assignable.All(definition =>
            !definition.Key.Equals(x, StringComparison.OrdinalIgnoreCase))))
        {
            throw new ValidationException("Every permission must exist in the permission catalog.");
        }

        if (!await roleCatalog.CanGrantPermissionsAsync(
                actor.Roles,
                actor.OrganizationId,
                result,
                ct))
        {
            throw new ForbiddenException("A role manager cannot grant permissions they do not hold.");
        }

        return result;
    }

    private async Task<IAsyncDisposable> AcquireLockAsync(string resource, CancellationToken ct)
    {
        var options = distributedLockOptions.Value;
        return await distributedLockProvider.TryAcquireAsync(
            resource,
            TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
            TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
            ct)
            ?? throw new ConflictException("IDENTITY_ADMINISTRATION_BUSY", "Identity administration resource is busy; retry the operation.");
    }

    private static RoleResponse ToResponse(IdentityRoleDocument role) =>
        new(
            role.Id,
            role.Name,
            string.IsNullOrWhiteSpace(role.DisplayName) ? role.Name : role.DisplayName,
            role.Description,
            role.Scope,
            role.OrganizationId,
            role.IsSystem,
            role.IsActive,
            role.IsProtected,
            role.IsDefault,
            role.DisplayOrder,
            role.Permissions,
            role.Version);
}
