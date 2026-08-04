namespace Zumbo.Modules.Organizations;

internal static class OrganizationResponseMapper
{
    internal static OrganizationResponse ToResponse(OrganizationDocument organization) =>
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
                        new DepartmentMemberResponse(member.UserId, member.Position)).ToList())).ToList(),
            organization.Status,
            organization.SuspendedAt,
            organization.ArchivedAt,
            organization.RetainUntil,
            organization.Version);
}
