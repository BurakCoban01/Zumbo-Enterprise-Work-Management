namespace Zumbo.Modules.Organizations;

public sealed record OrganizationMemberResponse(
    string UserId,
    string Position,
    string DepartmentId,
    string DepartmentName);
