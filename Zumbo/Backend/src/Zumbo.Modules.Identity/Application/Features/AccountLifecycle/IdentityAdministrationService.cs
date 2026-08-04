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
    IIdentityAuditWriter audit,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<RoleResponse>> ListRolesAsync(CancellationToken ct)
    {
        var actor = await GetActorAsync(ct);
        await EnsureSystemRolesAsync(ct);
        var result = await roles.ListByFilterAsync(
            x => x.IsSystem || x.OrganizationId == actor.OrganizationId,
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
        EnsureOrganizationScope(actor, organizationId);
        var name = NormalizeRoleName(request.Name);
        EnsureNotReserved(name);
        var permissions = NormalizePermissions(request.Permissions);
        await using var roleLock = await AcquireLockAsync("identity-roles:" + organizationId, ct);
        if (await roles.SelectAsync(x => x.OrganizationId == organizationId && x.Name.ToLower() == name.ToLower(), ct) is not null)
        {
            throw new ConflictException("ROLE_NAME_EXISTS", "Role name must be unique inside the organization.");
        }

        var now = clock.UtcNow;
        var role = new IdentityRoleDocument
        {
            Name = name,
            OrganizationId = organizationId,
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
        EnsureCustomRoleAccess(actor, role);
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
        role.Permissions = NormalizePermissions(request.Permissions);
        role.UpdatedAt = clock.UtcNow;
        await roles.ReplaceByFilterAsync(x => x.Id == role.Id, role, ct);
        await audit.WriteAsync("RoleUpdated", role.Id, oldValue, role.Name + ":" + string.Join(',', role.Permissions), correlationId, ct);
        return ToResponse(role);
    }

    public async Task DeleteRoleAsync(string roleId, string correlationId, CancellationToken ct)
    {
        var actor = await RequireRoleManagerAsync(ct);
        await using var roleLock = await AcquireLockAsync("identity-role:" + roleId, ct);
        var role = await roles.SelectAsync(x => x.Id == roleId, ct)
            ?? throw new NotFoundException("ROLE_NOT_FOUND", "Role was not found.");
        EnsureCustomRoleAccess(actor, role);
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
        EnsureOrganizationScope(actor, target.OrganizationId);
        var requestedRoles = (request.Roles ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!requestedRoles.Contains("User", StringComparer.OrdinalIgnoreCase))
        {
            requestedRoles.Insert(0, "User");
        }

        if (requestedRoles.Count > 20)
        {
            throw new ValidationException("A user cannot have more than 20 roles.");
        }

        await EnsureSystemRolesAsync(ct);
        var available = await roles.ListByFilterAsync(
            x => x.IsSystem || x.OrganizationId == target.OrganizationId,
            pageSize: 200,
            cancellationToken: ct);
        if (requestedRoles.Any(requested => available.All(x => !x.Name.Equals(requested, StringComparison.OrdinalIgnoreCase))))
        {
            throw new ValidationException("Every assigned role must be a defined system or organization role.");
        }

        var actorIsSystemAdmin = PermissionCatalog.IsSystemAdministrator(actor.Roles);
        if (!actorIsSystemAdmin)
        {
            if (target.Roles.Any(IsPrivilegedSystemRole)
                || requestedRoles.Any(IsPrivilegedSystemRole))
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
        await using var roleLock = await AcquireLockAsync("identity-system-roles", ct);
        foreach (var definition in PermissionCatalog.SystemRoles)
        {
            if (await roles.SelectAsync(x => x.IsSystem && x.Name == definition.Key, ct) is not null)
            {
                continue;
            }

            await roles.CreateAsync(new IdentityRoleDocument
            {
                Id = "system-role-" + definition.Key.ToLowerInvariant(),
                Name = definition.Key,
                IsSystem = true,
                Permissions = definition.Value.ToList(),
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow
            }, ct);
        }
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

    private static void EnsureOrganizationScope(UserDocument actor, string organizationId)
    {
        if (!PermissionCatalog.IsSystemAdministrator(actor.Roles)
            && actor.OrganizationId != organizationId)
        {
            throw new ForbiddenException("Role management is limited to the current organization.");
        }
    }

    private static void EnsureCustomRoleAccess(UserDocument actor, IdentityRoleDocument role)
    {
        if (role.IsSystem)
        {
            throw new ConflictException("SYSTEM_ROLE_LOCKED", "System roles cannot be changed or deleted.");
        }

        EnsureOrganizationScope(actor, role.OrganizationId!);
    }

    private static bool IsPrivilegedSystemRole(string role) =>
        role.Equals("SystemAdmin", StringComparison.OrdinalIgnoreCase)
        || role.Equals("OrganizationAdmin", StringComparison.OrdinalIgnoreCase)
        || role.Equals("AuditReader", StringComparison.OrdinalIgnoreCase);

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

    private static List<string> NormalizePermissions(IReadOnlyCollection<string>? permissions)
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

        if (result.Any(x => !PermissionCatalog.IsKnownAssignablePermission(x)))
        {
            throw new ValidationException("Every permission must exist in the permission catalog.");
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
        new(role.Id, role.Name, role.OrganizationId, role.IsSystem, role.Permissions);
}
