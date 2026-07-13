using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Infrastructure.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Organizations;

public sealed record CreateOrganizationRequest(string Name, string TenantKey);
public sealed record UpdateOrganizationRequest(string Name);
public sealed record CreateDepartmentRequest(string Name, string? ParentDepartmentId);
public sealed record UpdateDepartmentRequest(string Name, string? ParentDepartmentId);
public sealed record AssignDepartmentMemberRequest(string UserId, string Position);
public sealed record UpdateDepartmentMemberRequest(string Position);
public sealed record OrganizationResponse(string Id, string Name, string TenantKey, string OwnerUserId, IReadOnlyCollection<DepartmentResponse> Departments);
public sealed record DepartmentResponse(string Id, string Name, string? ParentDepartmentId, IReadOnlyCollection<DepartmentMemberResponse> Members);
public sealed record DepartmentMemberResponse(string UserId, string Position);

public sealed class OrganizationDocument : IDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string TenantKey { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public List<DepartmentDocument> Departments { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class DepartmentDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string? ParentDepartmentId { get; set; }
    public List<DepartmentMemberDocument> Members { get; set; } = [];
}

public sealed class DepartmentMemberDocument
{
    public string UserId { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
}

public interface IOrganizationMemberDirectory
{
    Task EnsureEligibleAsync(string userId, string organizationId, CancellationToken ct);
}

public interface IOrganizationAuditWriter
{
    Task WriteAsync(string action, string organizationId, string? oldValue, string? newValue, string correlationId, CancellationToken ct);
}

public sealed class OrganizationService(
    IDocumentRepository<OrganizationDocument> organizations,
    IOrganizationMemberDirectory memberDirectory,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    ICurrentUser currentUser,
    IOrganizationAuditWriter audit)
{
    public Task<OrganizationResponse> CreateAsync(CreateOrganizationRequest request, CancellationToken ct) =>
        CreateAsync(request, "none", ct);

    public async Task<OrganizationResponse> CreateAsync(CreateOrganizationRequest request, string correlationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.TenantKey))
        {
            throw new ValidationException("Organization name and tenant key are required.");
        }

        var tenantKey = request.TenantKey.Trim().ToLowerInvariant();
        ValidateTenantKey(tenantKey);
        var actorUserId = RequireCurrentUser();
        if (!IsSystemAdmin() && !string.Equals(currentUser.OrganizationId, tenantKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("An organization can only be created for the authenticated user's tenant.");
        }

        await using var organizationLock = await AcquireLockAsync("organization:" + tenantKey, ct);
        var duplicate = await organizations.SelectAsync(x => x.Id == tenantKey || x.TenantKey == tenantKey, ct);
        if (duplicate is not null)
        {
            throw new ConflictException("TENANT_KEY_EXISTS", "Tenant key must be unique.");
        }

        var organization = new OrganizationDocument
        {
            Id = tenantKey,
            Name = request.Name.Trim(),
            TenantKey = tenantKey,
            OwnerUserId = actorUserId,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };

        await organizations.CreateAsync(organization, ct);
        await audit.WriteAsync("OrganizationCreated", organization.Id, null, organization.Name, correlationId, ct);
        return ToResponse(organization);
    }

    public async Task<IReadOnlyList<OrganizationResponse>> ListAsync(CancellationToken ct)
    {
        RequireCurrentUser();
        var tenantId = currentUser.OrganizationId;
        var result = IsSystemAdmin()
            ? await organizations.ListByFilterAsync(orderBy: x => x.Name, pageSize: 100, cancellationToken: ct)
            : await organizations.ListByFilterAsync(
                x => x.Id == tenantId || x.TenantKey == tenantId,
                x => x.Name,
                pageSize: 1,
                cancellationToken: ct);
        return result.Select(ToResponse).ToList();
    }

    public Task<OrganizationResponse> UpdateAsync(string organizationId, UpdateOrganizationRequest request, CancellationToken ct) =>
        UpdateAsync(organizationId, request, "none", ct);

    public async Task<OrganizationResponse> UpdateAsync(string organizationId, UpdateOrganizationRequest request, string correlationId, CancellationToken ct)
    {
        var name = NormalizeName(request.Name, "Organization name");
        await using var organizationLock = await AcquireLockAsync("organization:" + organizationId, ct);
        var organization = await GetOrganization(organizationId, ct);
        EnsureCanManage(organization);
        var oldName = organization.Name;
        organization.Name = name;
        organization.UpdatedAt = clock.UtcNow;
        await organizations.ReplaceByFilterAsync(x => x.Id == organization.Id, organization, ct);
        await audit.WriteAsync("OrganizationUpdated", organization.Id, oldName, organization.Name, correlationId, ct);
        return ToResponse(organization);
    }

    public Task<OrganizationResponse> CreateDepartmentAsync(string organizationId, CreateDepartmentRequest request, CancellationToken ct) =>
        CreateDepartmentAsync(organizationId, request, "none", ct);

    public async Task<OrganizationResponse> CreateDepartmentAsync(string organizationId, CreateDepartmentRequest request, string correlationId, CancellationToken ct)
    {
        var name = NormalizeName(request.Name, "Department name");
        await using var organizationLock = await AcquireLockAsync("organization:" + organizationId, ct);
        var organization = await GetOrganization(organizationId, ct);
        EnsureCanManage(organization);
        if (organization.Departments.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("DEPARTMENT_EXISTS", "Department name must be unique inside the organization.");
        }

        if (!string.IsNullOrWhiteSpace(request.ParentDepartmentId)
            && organization.Departments.All(x => x.Id != request.ParentDepartmentId))
        {
            throw new ValidationException("Parent department must belong to the same organization.");
        }

        var department = new DepartmentDocument
        {
            Name = name,
            ParentDepartmentId = request.ParentDepartmentId
        };
        organization.Departments.Add(department);

        organization.UpdatedAt = clock.UtcNow;
        await organizations.ReplaceByFilterAsync(x => x.Id == organization.Id, organization, ct);
        await audit.WriteAsync("DepartmentCreated", organization.Id, null, $"{department.Id}:{department.Name}:{department.ParentDepartmentId}", correlationId, ct);
        return ToResponse(organization);
    }

    public async Task<OrganizationResponse> UpdateDepartmentAsync(
        string organizationId,
        string departmentId,
        UpdateDepartmentRequest request,
        CancellationToken ct)
        => await UpdateDepartmentAsync(organizationId, departmentId, request, "none", ct);

    public async Task<OrganizationResponse> UpdateDepartmentAsync(
        string organizationId,
        string departmentId,
        UpdateDepartmentRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var name = NormalizeName(request.Name, "Department name");
        await using var organizationLock = await AcquireLockAsync("organization:" + organizationId, ct);
        var organization = await GetOrganization(organizationId, ct);
        EnsureCanManage(organization);
        var department = GetDepartment(organization, departmentId);
        if (organization.Departments.Any(x =>
            x.Id != departmentId && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("DEPARTMENT_EXISTS", "Department name must be unique inside the organization.");
        }

        ValidateParent(organization, departmentId, request.ParentDepartmentId);
        var oldValue = $"{department.Id}:{department.Name}:{department.ParentDepartmentId}";
        department.Name = name;
        department.ParentDepartmentId = request.ParentDepartmentId;
        organization.UpdatedAt = clock.UtcNow;
        await organizations.ReplaceByFilterAsync(x => x.Id == organization.Id, organization, ct);
        await audit.WriteAsync("DepartmentUpdated", organization.Id, oldValue, $"{department.Id}:{department.Name}:{department.ParentDepartmentId}", correlationId, ct);
        return ToResponse(organization);
    }

    public async Task<OrganizationResponse> DeleteDepartmentAsync(
        string organizationId,
        string departmentId,
        CancellationToken ct)
        => await DeleteDepartmentAsync(organizationId, departmentId, "none", ct);

    public async Task<OrganizationResponse> DeleteDepartmentAsync(
        string organizationId,
        string departmentId,
        string correlationId,
        CancellationToken ct)
    {
        await using var organizationLock = await AcquireLockAsync("organization:" + organizationId, ct);
        var organization = await GetOrganization(organizationId, ct);
        EnsureCanManage(organization);
        var department = GetDepartment(organization, departmentId);
        if (organization.Departments.Any(x => x.ParentDepartmentId == departmentId))
        {
            throw new ConflictException("DEPARTMENT_HAS_CHILDREN", "A department with child departments cannot be deleted.");
        }

        if (department.Members.Count > 0)
        {
            throw new ConflictException("DEPARTMENT_HAS_MEMBERS", "A department with members cannot be deleted.");
        }

        organization.Departments.Remove(department);
        organization.UpdatedAt = clock.UtcNow;
        await organizations.ReplaceByFilterAsync(x => x.Id == organization.Id, organization, ct);
        await audit.WriteAsync("DepartmentDeleted", organization.Id, $"{department.Id}:{department.Name}:{department.ParentDepartmentId}", null, correlationId, ct);
        return ToResponse(organization);
    }

    public async Task<OrganizationResponse> AssignMemberAsync(
        string organizationId,
        string departmentId,
        AssignDepartmentMemberRequest request,
        CancellationToken ct)
        => await AssignMemberAsync(organizationId, departmentId, request, "none", ct);

    public async Task<OrganizationResponse> AssignMemberAsync(
        string organizationId,
        string departmentId,
        AssignDepartmentMemberRequest request,
        string correlationId,
        CancellationToken ct)
    {
        await using var organizationLock = await AcquireLockAsync("organization:" + organizationId, ct);
        var organization = await GetOrganization(organizationId, ct);
        EnsureCanManage(organization);
        await memberDirectory.EnsureEligibleAsync(request.UserId, organization.Id, ct);
        var department = GetDepartment(organization, departmentId);

        if (organization.Departments.SelectMany(x => x.Members).Any(x => x.UserId == request.UserId))
        {
            throw new ConflictException("DEPARTMENT_MEMBER_EXISTS", "User is already assigned to this department.");
        }

        department.Members.Add(new DepartmentMemberDocument
        {
            UserId = request.UserId,
            Position = NormalizePosition(request.Position)
        });

        organization.UpdatedAt = clock.UtcNow;
        await organizations.ReplaceByFilterAsync(x => x.Id == organization.Id, organization, ct);
        await audit.WriteAsync("DepartmentMemberAssigned", organization.Id, null, $"{department.Id}:{request.UserId}:{NormalizePosition(request.Position)}", correlationId, ct);
        return ToResponse(organization);
    }

    public async Task<OrganizationResponse> UpdateMemberPositionAsync(
        string organizationId,
        string departmentId,
        string userId,
        UpdateDepartmentMemberRequest request,
        CancellationToken ct)
        => await UpdateMemberPositionAsync(organizationId, departmentId, userId, request, "none", ct);

    public async Task<OrganizationResponse> UpdateMemberPositionAsync(
        string organizationId,
        string departmentId,
        string userId,
        UpdateDepartmentMemberRequest request,
        string correlationId,
        CancellationToken ct)
    {
        await using var organizationLock = await AcquireLockAsync("organization:" + organizationId, ct);
        var organization = await GetOrganization(organizationId, ct);
        EnsureCanManage(organization);
        var department = GetDepartment(organization, departmentId);
        var member = department.Members.SingleOrDefault(x => x.UserId == userId)
            ?? throw new NotFoundException("DEPARTMENT_MEMBER_NOT_FOUND", "Department member was not found.");
        var oldPosition = member.Position;
        member.Position = NormalizePosition(request.Position);
        organization.UpdatedAt = clock.UtcNow;
        await organizations.ReplaceByFilterAsync(x => x.Id == organization.Id, organization, ct);
        await audit.WriteAsync("DepartmentMemberPositionUpdated", organization.Id, $"{department.Id}:{userId}:{oldPosition}", $"{department.Id}:{userId}:{member.Position}", correlationId, ct);
        return ToResponse(organization);
    }

    public async Task<OrganizationResponse> RemoveMemberAsync(
        string organizationId,
        string departmentId,
        string userId,
        CancellationToken ct)
        => await RemoveMemberAsync(organizationId, departmentId, userId, "none", ct);

    public async Task<OrganizationResponse> RemoveMemberAsync(
        string organizationId,
        string departmentId,
        string userId,
        string correlationId,
        CancellationToken ct)
    {
        await using var organizationLock = await AcquireLockAsync("organization:" + organizationId, ct);
        var organization = await GetOrganization(organizationId, ct);
        EnsureCanManage(organization);
        var department = GetDepartment(organization, departmentId);
        var member = department.Members.SingleOrDefault(x => x.UserId == userId);
        if (member is null)
        {
            throw new NotFoundException("DEPARTMENT_MEMBER_NOT_FOUND", "Department member was not found.");
        }
        department.Members.Remove(member);

        organization.UpdatedAt = clock.UtcNow;
        await organizations.ReplaceByFilterAsync(x => x.Id == organization.Id, organization, ct);
        await audit.WriteAsync("DepartmentMemberRemoved", organization.Id, $"{department.Id}:{userId}:{member.Position}", null, correlationId, ct);
        return ToResponse(organization);
    }

    private async Task<OrganizationDocument> GetOrganization(string organizationId, CancellationToken ct) =>
        await organizations.SelectAsync(x => x.Id == organizationId || x.TenantKey == organizationId, ct)
        ?? throw new NotFoundException("ORGANIZATION_NOT_FOUND", "Organization was not found.");

    private static DepartmentDocument GetDepartment(OrganizationDocument organization, string departmentId) =>
        organization.Departments.SingleOrDefault(x => x.Id == departmentId)
        ?? throw new NotFoundException("DEPARTMENT_NOT_FOUND", "Department was not found.");

    private void EnsureCanManage(OrganizationDocument organization)
    {
        var actorUserId = RequireCurrentUser();
        var belongsToTenant = string.Equals(currentUser.OrganizationId, organization.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(currentUser.OrganizationId, organization.TenantKey, StringComparison.OrdinalIgnoreCase);
        var canManage = IsSystemAdmin()
            || (belongsToTenant
                && (string.Equals(organization.OwnerUserId, actorUserId, StringComparison.Ordinal)
                    || currentUser.Roles.Contains("OrganizationAdmin", StringComparer.OrdinalIgnoreCase)));
        if (!canManage)
        {
            throw new ForbiddenException("Organization management permission is required.");
        }
    }

    private string RequireCurrentUser()
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        return currentUser.UserId;
    }

    private bool IsSystemAdmin() =>
        currentUser.Roles.Contains("SystemAdmin", StringComparer.OrdinalIgnoreCase);

    private static void ValidateParent(
        OrganizationDocument organization,
        string departmentId,
        string? parentDepartmentId)
    {
        if (string.IsNullOrWhiteSpace(parentDepartmentId))
        {
            return;
        }

        if (parentDepartmentId == departmentId)
        {
            throw new ValidationException("A department cannot be its own parent.");
        }

        var byId = organization.Departments.ToDictionary(x => x.Id, StringComparer.Ordinal);
        if (!byId.TryGetValue(parentDepartmentId, out var parent))
        {
            throw new ValidationException("Parent department must belong to the same organization.");
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (parent is not null)
        {
            if (parent.Id == departmentId)
            {
                throw new ConflictException("DEPARTMENT_HIERARCHY_CYCLE", "Department hierarchy cannot contain a cycle.");
            }

            if (!visited.Add(parent.Id) || string.IsNullOrWhiteSpace(parent.ParentDepartmentId))
            {
                break;
            }

            byId.TryGetValue(parent.ParentDepartmentId, out parent);
        }
    }

    private static string NormalizeName(string? name, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException(fieldName + " is required.");
        }

        var normalized = name.Trim();
        if (normalized.Length > 120)
        {
            throw new ValidationException(fieldName + " cannot exceed 120 characters.");
        }

        return normalized;
    }

    private static string NormalizePosition(string? position)
    {
        var normalized = string.IsNullOrWhiteSpace(position) ? "Member" : position.Trim();
        if (normalized.Length > 120)
        {
            throw new ValidationException("Position cannot exceed 120 characters.");
        }

        return normalized;
    }

    private static void ValidateTenantKey(string tenantKey)
    {
        if (tenantKey.Length is < 3 or > 64
            || tenantKey.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ValidationException("Tenant key must be 3-64 characters and contain only letters, numbers, hyphens or underscores.");
        }
    }

    private async Task<IAsyncDisposable> AcquireLockAsync(string resource, CancellationToken ct)
    {
        var options = distributedLockOptions.Value;
        return await distributedLockProvider.TryAcquireAsync(
            resource,
            TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
            TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
            ct)
            ?? throw new ConflictException("ORGANIZATION_RESOURCE_BUSY", "The organization is busy; retry the operation.");
    }

    private static OrganizationResponse ToResponse(OrganizationDocument organization) =>
        new(
            organization.Id,
            organization.Name,
            organization.TenantKey,
            organization.OwnerUserId,
            organization.Departments.Select(department =>
                new DepartmentResponse(
                    department.Id,
                    department.Name,
                    department.ParentDepartmentId,
                    department.Members.Select(member =>
                        new DepartmentMemberResponse(member.UserId, member.Position)).ToList())).ToList());
}
