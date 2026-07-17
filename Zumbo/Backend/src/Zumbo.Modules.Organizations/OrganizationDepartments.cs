using Zumbo.SharedKernel;

namespace Zumbo.Modules.Organizations;

public sealed partial class OrganizationService
{
    public Task<OrganizationResponse> CreateDepartmentAsync(
        string organizationId,
        CreateDepartmentRequest request,
        CancellationToken ct) =>
        CreateDepartmentAsync(organizationId, request, "none", ct);

    public async Task<OrganizationResponse> CreateDepartmentAsync(
        string organizationId,
        CreateDepartmentRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var name = NormalizeName(request.Name, "Department name");
        await using var organizationLock = await AcquireLockAsync("organization:" + organizationId, ct);
        var organization = await GetOrganization(organizationId, ct);
        EnsureCanManage(organization);
        EnsureActive(organization);
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
        await SaveAsync(organization, ct);
        await audit.WriteAsync(
            "DepartmentCreated",
            organization.Id,
            null,
            $"{department.Id}:{department.Name}:{department.ParentDepartmentId}",
            correlationId,
            ct);
        return ToResponse(organization);
    }

    public Task<OrganizationResponse> UpdateDepartmentAsync(
        string organizationId,
        string departmentId,
        UpdateDepartmentRequest request,
        CancellationToken ct) =>
        UpdateDepartmentAsync(organizationId, departmentId, request, "none", ct);

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
        EnsureActive(organization);
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
        await SaveAsync(organization, ct);
        await audit.WriteAsync(
            "DepartmentUpdated",
            organization.Id,
            oldValue,
            $"{department.Id}:{department.Name}:{department.ParentDepartmentId}",
            correlationId,
            ct);
        return ToResponse(organization);
    }

    public Task<OrganizationResponse> DeleteDepartmentAsync(
        string organizationId,
        string departmentId,
        CancellationToken ct) =>
        DeleteDepartmentAsync(organizationId, departmentId, "none", ct);

    public async Task<OrganizationResponse> DeleteDepartmentAsync(
        string organizationId,
        string departmentId,
        string correlationId,
        CancellationToken ct)
    {
        await using var organizationLock = await AcquireLockAsync("organization:" + organizationId, ct);
        var organization = await GetOrganization(organizationId, ct);
        EnsureCanManage(organization);
        EnsureActive(organization);
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
        await SaveAsync(organization, ct);
        await audit.WriteAsync(
            "DepartmentDeleted",
            organization.Id,
            $"{department.Id}:{department.Name}:{department.ParentDepartmentId}",
            null,
            correlationId,
            ct);
        return ToResponse(organization);
    }

    public Task<OrganizationResponse> AssignMemberAsync(
        string organizationId,
        string departmentId,
        AssignDepartmentMemberRequest request,
        CancellationToken ct) =>
        AssignMemberAsync(organizationId, departmentId, request, "none", ct);

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
        EnsureActive(organization);
        await memberDirectory.EnsureEligibleAsync(request.UserId, organization.Id, ct);
        var department = GetDepartment(organization, departmentId);
        if (organization.Departments.SelectMany(x => x.Members).Any(x => x.UserId == request.UserId))
        {
            throw new ConflictException("DEPARTMENT_MEMBER_EXISTS", "User is already assigned to this department.");
        }

        var position = NormalizePosition(request.Position);
        department.Members.Add(new DepartmentMemberDocument
        {
            UserId = request.UserId,
            Position = position
        });
        await SaveAsync(organization, ct);
        await audit.WriteAsync(
            "DepartmentMemberAssigned",
            organization.Id,
            null,
            $"{department.Id}:{request.UserId}:{position}",
            correlationId,
            ct);
        return ToResponse(organization);
    }

    public Task<OrganizationResponse> UpdateMemberPositionAsync(
        string organizationId,
        string departmentId,
        string userId,
        UpdateDepartmentMemberRequest request,
        CancellationToken ct) =>
        UpdateMemberPositionAsync(organizationId, departmentId, userId, request, "none", ct);

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
        EnsureActive(organization);
        var department = GetDepartment(organization, departmentId);
        var member = department.Members.SingleOrDefault(x => x.UserId == userId)
            ?? throw new NotFoundException("DEPARTMENT_MEMBER_NOT_FOUND", "Department member was not found.");
        var oldPosition = member.Position;
        member.Position = NormalizePosition(request.Position);
        await SaveAsync(organization, ct);
        await audit.WriteAsync(
            "DepartmentMemberPositionUpdated",
            organization.Id,
            $"{department.Id}:{userId}:{oldPosition}",
            $"{department.Id}:{userId}:{member.Position}",
            correlationId,
            ct);
        return ToResponse(organization);
    }

    public Task<OrganizationResponse> RemoveMemberAsync(
        string organizationId,
        string departmentId,
        string userId,
        CancellationToken ct) =>
        RemoveMemberAsync(organizationId, departmentId, userId, "none", ct);

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
        EnsureActive(organization);
        var department = GetDepartment(organization, departmentId);
        var member = department.Members.SingleOrDefault(x => x.UserId == userId)
            ?? throw new NotFoundException("DEPARTMENT_MEMBER_NOT_FOUND", "Department member was not found.");
        department.Members.Remove(member);
        await SaveAsync(organization, ct);
        await audit.WriteAsync(
            "DepartmentMemberRemoved",
            organization.Id,
            $"{department.Id}:{userId}:{member.Position}",
            null,
            correlationId,
            ct);
        return ToResponse(organization);
    }

    private static DepartmentDocument GetDepartment(OrganizationDocument organization, string departmentId) =>
        organization.Departments.SingleOrDefault(x => x.Id == departmentId)
        ?? throw new NotFoundException("DEPARTMENT_NOT_FOUND", "Department was not found.");

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

    private static string NormalizePosition(string? position)
    {
        var normalized = string.IsNullOrWhiteSpace(position) ? "Member" : position.Trim();
        if (normalized.Length > 120)
        {
            throw new ValidationException("Position cannot exceed 120 characters.");
        }

        return normalized;
    }
}
